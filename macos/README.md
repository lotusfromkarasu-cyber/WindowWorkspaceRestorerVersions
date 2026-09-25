# Window Workspace Restorer for macOS

SwiftUI macOS companion to the Windows WPF release. It keeps saved workspaces in `~/Library/Application Support/WindowWorkspaceRestorer/workspaces-macos.json`.

## Features

- Save and select multiple workspaces.
- Capture document and folder locations from Finder and supported Microsoft Office, WPS, and COMSOL windows when macOS exposes their accessibility document URLs.
- Add files and folders manually, select individual entries, then restore documents with their default macOS application.
- Restore selected Finder folders together as tabs when Finder and macOS automation permissions allow it.
- Show a menu bar control for opening the main window, saving the current workspace, and quitting.

On first capture, allow the app to control System Events and access Accessibility information in System Settings → Privacy & Security → Automation and Accessibility. App scripting and document-path availability vary by the installed Office, WPS, and COMSOL versions. If a window does not expose its document URL, add that document manually.

The app targets macOS 13 or later and is built separately for Apple Silicon (`arm64`) and Intel (`x86_64`). The GitHub Actions workflow creates an ad-hoc signed `.app` zip on GitHub-hosted macOS runners; it does not install macOS build tools on Windows.
