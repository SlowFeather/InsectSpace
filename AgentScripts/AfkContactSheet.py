"""Produce labelled contact sheets for local Unity camera captures."""
import argparse
from pathlib import Path
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser()
parser.add_argument("folder", type=Path)
parser.add_argument("--prefix", default="")
parser.add_argument("--columns", type=int, default=4)
args = parser.parse_args()
paths = sorted(args.folder.glob(args.prefix + "*.png"), key=lambda p: tuple(map(float, p.stem.split("-"))))
width, height = 224, 420
sheet = Image.new("RGB", (args.columns * width, ((len(paths) + args.columns - 1) // args.columns) * height), "#222222")
draw = ImageDraw.Draw(sheet)
for i, path in enumerate(paths):
    x, y = (i % args.columns) * width, (i // args.columns) * height
    draw.text((x + 4, y + 3), path.stem, fill="white")
    picture = Image.open(path).convert("RGB")
    picture.thumbnail((width, height - 22))
    sheet.paste(picture, (x, y + 22))
target = args.folder.parent / (args.folder.name + "-" + args.prefix.strip("-") + "-contact.jpg")
sheet.save(target, quality=90)
print(target)
