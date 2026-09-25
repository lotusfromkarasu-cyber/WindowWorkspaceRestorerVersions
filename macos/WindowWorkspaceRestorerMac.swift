import AppKit
import SwiftUI

enum ItemKind: String, Codable {
    case document
    case folder
}

struct WorkspaceItem: Codable, Identifiable {
    var id = UUID()
    var appName: String
    var bundleIdentifier: String?
    var title: String
    var location: String
    var kind: ItemKind
    var selected = true
}

struct Workspace: Codable, Identifiable {
    var id = UUID()
    var name: String
    var createdAt = Date()
    var items: [WorkspaceItem]
}

enum MacAutomation {
    static let captureScript = #"
    tell application "System Events"
        set resultText to ""
        set rowSeparator to ASCII character 9
        set lineSeparator to ASCII character 10
        set trackedNames to {"Finder", "Microsoft Word", "Microsoft Excel", "Microsoft PowerPoint", "WPS Office", "WPS Writer", "WPS Spreadsheet", "WPS Presentation", "COMSOL Multiphysics"}
        repeat with appProcess in (application processes whose background only is false)
            set processName to name of appProcess
            if processName is in trackedNames or processName contains "WPS" or processName contains "COMSOL" then
                repeat with appWindow in windows of appProcess
                    try
                        set windowTitle to name of appWindow
                        set documentLocation to ""
                        try
                            set documentLocation to value of attribute "AXDocument" of appWindow as text
                        end try
                        if documentLocation is not "" then
                            set resultText to resultText & processName & rowSeparator & windowTitle & rowSeparator & documentLocation & lineSeparator
                        end if
                    end try
                end repeat
            end if
        end repeat
        return resultText
    end tell
    "#

    static func capture(bundleIdentifiers: [String: String]) throws -> [WorkspaceItem] {
        let output = try runAppleScript(captureScript)
        var seen = Set<String>()
        return output.split(whereSeparator: \.isNewline).compactMap { row in
            let fields = row.split(separator: "\t", maxSplits: 2, omittingEmptySubsequences: false).map(String.init)
            guard fields.count == 3, let location = normalizedLocation(fields[2]) else { return nil }
            let key = fields[0] + "\u{0}" + location
            guard seen.insert(key).inserted else { return nil }
            let kind = isFolder(location) ? ItemKind.folder : ItemKind.document
            return WorkspaceItem(
                appName: fields[0],
                bundleIdentifier: bundleIdentifiers[fields[0]],
                title: fields[1].isEmpty ? URL(fileURLWithPath: location).lastPathComponent : fields[1],
                location: location,
                kind: kind
            )
        }
    }

    static func restoreFinderFolders(_ paths: [String]) throws {
        guard !paths.isEmpty else { return }
        let folderList = paths.map(appleScriptString).joined(separator: ", ")
        let script = """
        tell application "Finder"
            set previousTabSetting to folders open in new tabs of Finder preferences
            try
                set folders open in new tabs of Finder preferences to true
                set folderPaths to {\(folderList)}
                repeat with folderPath in folderPaths
                    open (POSIX file (contents of folderPath))
                end repeat
            on error errorMessage number errorNumber
                set folders open in new tabs of Finder preferences to previousTabSetting
                error errorMessage number errorNumber
            end try
            set folders open in new tabs of Finder preferences to previousTabSetting
        end tell
        """
        _ = try runAppleScript(script)
    }

    static func runAppleScript(_ script: String) throws -> String {
        let execute = { () throws -> String in
            guard let appleScript = NSAppleScript(source: script) else {
                throw NSError(domain: "WindowWorkspaceRestorerMac", code: 1, userInfo: [NSLocalizedDescriptionKey: "AppleScript 无法解析。"])
            }
            var error: NSDictionary?
            let result = appleScript.executeAndReturnError(&error)
            if let error {
                let message = error[NSAppleScript.errorMessage] as? String ?? "macOS 自动化操作失败，请检查“隐私与安全性”中的自动化和辅助功能权限。"
                throw NSError(domain: "WindowWorkspaceRestorerMac", code: 2, userInfo: [NSLocalizedDescriptionKey: message])
            }
            return result.stringValue ?? ""
        }
        return Thread.isMainThread ? try execute() : try DispatchQueue.main.sync(execute: execute)
    }

    static func normalizedLocation(_ value: String) -> String? {
        let candidate = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !candidate.isEmpty else { return nil }
        if let url = URL(string: candidate), let scheme = url.scheme {
            if scheme == "file" { return url.standardizedFileURL.path }
            if scheme == "http" || scheme == "https" { return candidate }
            return nil
        }
        let expanded = (candidate as NSString).expandingTildeInPath
        guard expanded.hasPrefix("/") else { return nil }
        return URL(fileURLWithPath: expanded).standardizedFileURL.path
    }

    static func isFolder(_ location: String) -> Bool {
        guard !location.hasPrefix("http://"), !location.hasPrefix("https://") else { return false }
        var isDirectory = ObjCBool(false)
        return FileManager.default.fileExists(atPath: location, isDirectory: &isDirectory) && isDirectory.boolValue
    }

    static func appleScriptString(_ value: String) -> String {
        "\"" + value.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
    }

    static func fileURL(_ location: String) -> URL? {
        if let url = URL(string: location), let scheme = url.scheme, scheme != "file" {
            return url
        }
        return URL(fileURLWithPath: location)
    }

    static func open(_ item: WorkspaceItem) -> Bool {
        guard let url = fileURL(item.location) else { return false }
        let openOnMain: () -> Bool = {
            if let bundleID = item.bundleIdentifier,
               let appURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) {
                NSWorkspace.shared.open([url], withApplicationAt: appURL, configuration: NSWorkspace.OpenConfiguration(), completionHandler: nil)
                return true
            }
            return NSWorkspace.shared.open(url)
        }
        return Thread.isMainThread ? openOnMain() : DispatchQueue.main.sync(execute: openOnMain)
    }
}

@MainActor
final class WorkspaceStore: ObservableObject {
    @Published var workspaces: [Workspace] = []
    @Published var selectedID: UUID?
    @Published var status = "就绪"

    private let fileURL: URL

    init() {
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("WindowWorkspaceRestorer", isDirectory: true)
        try? FileManager.default.createDirectory(at: support, withIntermediateDirectories: true, attributes: nil)
        fileURL = support.appendingPathComponent("workspaces-macos.json")
        if let data = try? Data(contentsOf: fileURL),
           let saved = try? JSONDecoder.pretty.decode([Workspace].self, from: data) {
            workspaces = saved
            selectedID = saved.first?.id
        }
    }

    var selectedWorkspace: Workspace? {
        workspaces.first { $0.id == selectedID }
    }

    func captureWorkspace() {
        status = "正在扫描 Finder、Office、WPS 和 COMSOL 窗口…"
        let bundleIdentifiers = Dictionary(
            NSWorkspace.shared.runningApplications.compactMap { app -> (String, String)? in
                guard let name = app.localizedName, let bundleID = app.bundleIdentifier else { return nil }
                return (name, bundleID)
            },
            uniquingKeysWith: { first, _ in first }
        )
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let items = try MacAutomation.capture(bundleIdentifiers: bundleIdentifiers)
                DispatchQueue.main.async {
                    let date = DateFormatter.localizedString(from: Date(), dateStyle: .medium, timeStyle: .short)
                    let workspace = Workspace(name: "工作区 \(date)", items: items)
                    self.workspaces.insert(workspace, at: 0)
                    self.selectedID = workspace.id
                    self.persist()
                    self.status = items.isEmpty
                        ? "已创建空工作区。未读取到窗口路径时，可以手动添加文件和文件夹。"
                        : "已保存 \(items.count) 个窗口或文档。"
                }
            } catch {
                DispatchQueue.main.async { self.status = error.localizedDescription }
            }
        }
    }

    func addFilesAndFolders() {
        guard let workspaceIndex = workspaces.firstIndex(where: { $0.id == selectedID }) else { return }
        let panel = NSOpenPanel()
        panel.title = "添加要恢复的文件或文件夹"
        panel.message = "选择 Office、WPS、COMSOL 文档或 Finder 文件夹。"
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true
        guard panel.runModal() == .OK else { return }
        for url in panel.urls {
            var isDirectory = ObjCBool(false)
            _ = FileManager.default.fileExists(atPath: url.path, isDirectory: &isDirectory)
            let appURL = NSWorkspace.shared.urlForApplication(toOpen: url)
            let appName = appURL.flatMap { Bundle(url: $0)?.object(forInfoDictionaryKey: "CFBundleName") as? String } ?? (isDirectory.boolValue ? "Finder" : "默认应用")
            let bundleID = appURL.flatMap { Bundle(url: $0)?.bundleIdentifier }
            workspaces[workspaceIndex].items.append(WorkspaceItem(
                appName: appName,
                bundleIdentifier: bundleID,
                title: url.lastPathComponent,
                location: url.standardizedFileURL.path,
                kind: isDirectory.boolValue ? .folder : .document
            ))
        }
        persist()
        status = "已添加所选文件和文件夹。"
    }

    func setSelected(_ itemID: UUID, selected: Bool) {
        guard let workspaceIndex = workspaces.firstIndex(where: { $0.id == selectedID }),
              let itemIndex = workspaces[workspaceIndex].items.firstIndex(where: { $0.id == itemID }) else { return }
        workspaces[workspaceIndex].items[itemIndex].selected = selected
        persist()
    }

    func renameWorkspace(_ id: UUID, name: String) {
        guard let index = workspaces.firstIndex(where: { $0.id == id }) else { return }
        workspaces[index].name = name
        persist()
    }

    func selectAll(_ selected: Bool) {
        guard let workspaceIndex = workspaces.firstIndex(where: { $0.id == selectedID }) else { return }
        for index in workspaces[workspaceIndex].items.indices {
            workspaces[workspaceIndex].items[index].selected = selected
        }
        persist()
    }

    func removeItem(_ itemID: UUID) {
        guard let workspaceIndex = workspaces.firstIndex(where: { $0.id == selectedID }) else { return }
        workspaces[workspaceIndex].items.removeAll { $0.id == itemID }
        persist()
    }

    func deleteWorkspace(_ id: UUID) {
        workspaces.removeAll { $0.id == id }
        if selectedID == id { selectedID = workspaces.first?.id }
        persist()
    }

    func restoreSelected() {
        guard let workspace = selectedWorkspace else { return }
        let items = workspace.items.filter(\.selected)
        guard !items.isEmpty else { status = "请先选择要恢复的项目。"; return }
        status = "正在恢复所选项目…"
        DispatchQueue.global(qos: .userInitiated).async {
            let folders = items.filter { $0.kind == .folder && !$0.location.hasPrefix("http") }
            let documents = items.filter { $0.kind == .document || $0.location.hasPrefix("http") }
            var opened = 0
            var failed = 0
            do {
                try MacAutomation.restoreFinderFolders(folders.map(\.location))
                opened += folders.count
            } catch {
                for item in folders {
                    if MacAutomation.open(item) { opened += 1 } else { failed += 1 }
                }
            }
            for item in documents {
                if MacAutomation.open(item) { opened += 1 } else { failed += 1 }
                Thread.sleep(forTimeInterval: 0.08)
            }
            DispatchQueue.main.async {
                self.status = failed == 0 ? "已提交 \(opened) 个项目到对应 Mac 应用。" : "已打开 \(opened) 项，\(failed) 项无法打开；请检查文件路径和应用权限。"
            }
        }
    }

    private func persist() {
        do {
            let data = try JSONEncoder.pretty.encode(workspaces)
            try data.write(to: fileURL, options: .atomic)
        } catch {
            status = "保存工作区失败：\(error.localizedDescription)"
        }
    }
}

extension JSONEncoder {
    static var pretty: JSONEncoder {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601
        return encoder
    }
}

extension JSONDecoder {
    static var pretty: JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }
}

struct MainView: View {
    @EnvironmentObject private var store: WorkspaceStore

    var body: some View {
        NavigationSplitView {
            VStack(spacing: 0) {
                HStack {
                    Label("工作区", systemImage: "rectangle.on.rectangle")
                        .font(.headline)
                    Spacer()
                    Button(action: store.captureWorkspace) {
                        Image(systemName: "plus")
                    }
                    .help("扫描并保存当前工作区")
                }
                .padding()

                List(selection: $store.selectedID) {
                    ForEach(store.workspaces) { workspace in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(workspace.name).lineLimit(1)
                            Text("\(workspace.items.count) 个项目")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                        .tag(workspace.id)
                        .contextMenu {
                            Button("删除工作区", role: .destructive) { store.deleteWorkspace(workspace.id) }
                        }
                    }
                }
                .listStyle(.sidebar)

                Button(action: store.captureWorkspace) {
                    Label("扫描并保存", systemImage: "rectangle.stack.badge.plus")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .padding()
            }
            .navigationSplitViewColumnWidth(min: 220, ideal: 260)
        } detail: {
            detail
        }
        .frame(minWidth: 900, minHeight: 560)
    }

    @ViewBuilder
    private var detail: some View {
        if let workspace = store.selectedWorkspace {
            VStack(spacing: 0) {
                HStack {
                    VStack(alignment: .leading, spacing: 4) {
                        TextField("工作区名称", text: Binding(
                            get: { workspace.name },
                            set: { store.renameWorkspace(workspace.id, name: $0) }
                        ))
                        .font(.title2.bold())
                        .textFieldStyle(.plain)
                        Text("选择要恢复的文档和文件夹")
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("添加文件或文件夹", systemImage: "plus") { store.addFilesAndFolders() }
                    Button("删除", systemImage: "trash", role: .destructive) { store.deleteWorkspace(workspace.id) }
                }
                .padding()

                HStack {
                    Button("全选") { store.selectAll(true) }
                    Button("清空选择") { store.selectAll(false) }
                    Spacer()
                    Text("已选 \(workspace.items.filter(\.selected).count) / \(workspace.items.count)")
                        .foregroundStyle(.secondary)
                }
                .padding(.horizontal)
                .padding(.bottom, 8)

                List {
                    ForEach(workspace.items) { item in
                        Toggle(isOn: Binding(
                            get: { item.selected },
                            set: { store.setSelected(item.id, selected: $0) }
                        )) {
                            HStack(spacing: 12) {
                                Image(systemName: item.kind == .folder ? "folder.fill" : "doc.text")
                                    .foregroundColor(item.kind == .folder ? .blue : .secondary)
                                    .frame(width: 22)
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(item.title).lineLimit(1)
                                    Text("\(item.appName) · \(item.location)")
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                        .lineLimit(1)
                                }
                                Spacer(minLength: 4)
                                Button(role: .destructive) { store.removeItem(item.id) } label: {
                                    Image(systemName: "minus.circle")
                                }
                                .buttonStyle(.borderless)
                                .help("从工作区移除")
                            }
                        }
                        .toggleStyle(.checkbox)
                    }
                }
                .listStyle(.inset)

                HStack {
                    Text(store.status)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                    Spacer()
                    Button(action: store.restoreSelected) {
                        Label("恢复所选项目", systemImage: "arrow.uturn.backward")
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(workspace.items.isEmpty)
                }
                .padding()
            }
        } else {
            VStack(spacing: 12) {
                Image(systemName: "rectangle.on.rectangle")
                    .font(.system(size: 42))
                    .foregroundStyle(.secondary)
                Text("暂无已保存工作区").font(.title2)
                Text("扫描 Finder、Microsoft Office、WPS 和 COMSOL 的已打开窗口，或先扫描创建工作区，再手动添加文件与文件夹。")
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: 520)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

@main
struct WindowWorkspaceRestorerMacApp: App {
    @StateObject private var store = WorkspaceStore()

    var body: some Scene {
        WindowGroup("Window Workspace Restorer") {
            MainView()
                .environmentObject(store)
        }
        .defaultSize(width: 1120, height: 720)

        MenuBarExtra("工作区恢复", systemImage: "rectangle.on.rectangle") {
            Button("显示工作区") {
                NSApp.activate(ignoringOtherApps: true)
                NSApp.windows.first?.makeKeyAndOrderFront(nil)
            }
            Button("扫描并保存") { store.captureWorkspace() }
            Divider()
            Button("退出") { NSApp.terminate(nil) }
        }
    }
}
