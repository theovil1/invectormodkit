"""Makes the Discord rich presence art assets: logo.png from the game's official Steam art (the cover's planet
behind the official logo), and one icon per difficulty drawn in big square pixels.

Run: python docs/discord/make_assets.py (needs Pillow). Upload the PNGs to the Discord application's
Rich Presence > Art Assets, under their file names without the extension.
"""
import math
import os
from PIL import Image

OUT = os.path.dirname(os.path.abspath(__file__))

WHITE = (255, 255, 255)


def save(grid, size, name):
    """grid: list of rows of RGB tuples (or None for transparent), scaled up with no smoothing."""
    n = len(grid)
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    for y, row in enumerate(grid):
        for x, c in enumerate(row):
            if c is not None:
                img.putpixel((x, y), c + (255,))
    img.resize((size, size), Image.NEAREST).save(os.path.join(OUT, name))


STEAM_ART = "C:/Program Files (x86)/Steam/appcache/librarycache/2009450"


def official_logo(size=1024):
    """A square of the Steam library background around its planet, with the official logo across it."""
    hero = Image.open(os.path.join(STEAM_ART, "library_hero.jpg")).convert("RGBA")
    square = hero.crop((650, 0, 1270, 620)).resize((size, size), Image.LANCZOS)
    text = Image.open(os.path.join(STEAM_ART, "logo.png")).convert("RGBA")
    width = int(size * 0.88)
    text = text.resize((width, int(text.height * width / text.width)), Image.LANCZOS)
    square.alpha_composite(text, ((size - width) // 2, int(size * 0.36)))
    square.convert("RGB").save(os.path.join(OUT, "logo.png"))


# The marks of each difficulty: 1 to 3 rising bars, then a star for Ultra.
DIFFICULTIES = {
    "casual": ((61, 220, 132), ["..........", "..........", "..........", "..........", "....XX....", "....XX....", "....XX....", "....XX....", "..........", ".........."]),
    "normal": ((59, 167, 255), ["..........", "..........", "..........", ".....XX...", ".....XX...", "..XX.XX...", "..XX.XX...", "..XX.XX...", "..........", ".........."]),
    "hard": ((255, 138, 31), ["..........", "..........", ".......XX.", ".......XX.", "....XX.XX.", "....XX.XX.", ".XX.XX.XX.", ".XX.XX.XX.", "..........", ".........."]),
    "ultra": ((224, 47, 176), ["....XX....", "....XX....", "...XXXX...", "XXXXXXXXXX", ".XXXXXXXX.", "..XXXXXX..", "..XXXXXX..", ".XXX..XXX.", ".XX....XX.", ".........."]),
}


def icon(color, mark):
    n = 16
    g = [[None] * n for _ in range(n)]
    c = 7.5
    dark = tuple(int(v * 0.55) for v in color)
    for y in range(n):
        for x in range(n):
            d = math.hypot(x - c, y - c)
            if d <= 7.9:
                g[y][x] = dark if d > 6.6 else color
    for j, row in enumerate(mark):
        for i, ch in enumerate(row):
            if ch == "X":
                g[3 + j][3 + i] = WHITE
    return g


if __name__ == "__main__":
    official_logo()
    for name, (color, mark) in DIFFICULTIES.items():
        save(icon(color, mark), 512, name + ".png")
    print("saved in", OUT)
