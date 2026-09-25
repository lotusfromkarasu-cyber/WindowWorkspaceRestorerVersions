from pathlib import Path

from PIL import Image, ImageDraw


SIZE = 256
SCALE = 4
image = Image.new("RGBA", (SIZE * SCALE, SIZE * SCALE), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)


def rect(box, radius, fill, outline=None, width=1):
    scaled = tuple(int(value * SCALE) for value in box)
    draw.rounded_rectangle(
        scaled,
        radius=radius * SCALE,
        fill=fill,
        outline=outline,
        width=width * SCALE,
    )


def line(points, fill, width):
    draw.line(
        [(int(x * SCALE), int(y * SCALE)) for x, y in points],
        fill=fill,
        width=width * SCALE,
        joint="curve",
    )


navy = "#163B65"
blue = "#2B78D0"
light_blue = "#8CC8FF"
orange = "#F2B84B"
white = "#FFFFFF"
muted = "#D9E8F7"

# Folder silhouette.
draw.polygon(
    [(32 * SCALE, 65 * SCALE), (91 * SCALE, 65 * SCALE),
     (108 * SCALE, 84 * SCALE), (219 * SCALE, 84 * SCALE),
     (229 * SCALE, 101 * SCALE), (30 * SCALE, 101 * SCALE)],
    fill=orange,
)
rect((25, 78, 231, 211), 23, blue, navy, 6)
line([(44, 102), (214, 102)], light_blue, 5)

# Window/tab sheet layered above the folder.
rect((64, 29, 221, 168), 18, white, navy, 6)
draw.rectangle((67 * SCALE, 32 * SCALE, 218 * SCALE, 65 * SCALE), fill=navy)
rect((80, 43, 112, 57), 6, orange)
rect((120, 43, 151, 57), 6, blue)
rect((159, 43, 190, 57), 6, light_blue)

# Three folder tabs in the content area.
rect((82, 80, 121, 101), 7, blue)
rect((128, 80, 167, 101), 7, light_blue)
rect((174, 80, 203, 101), 7, orange)

line([(83, 119), (202, 119)], navy, 6)
line([(83, 137), (176, 137)], muted, 7)
line([(83, 153), (153, 153)], muted, 7)

# Downsample for clean edges and embed common Windows icon sizes.
image = image.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
output = Path(__file__).resolve().parents[1] / "src" / "WindowWorkspaceRestorer.App" / "app.ico"
image.save(output, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(output)
