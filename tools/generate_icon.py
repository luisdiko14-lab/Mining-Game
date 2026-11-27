#!/usr/bin/env python3
"""Generate the original BlockCraft Windows application icon."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets" / "generated" / "BlockCraft.ico"
SIZE = 512
im = Image.new("RGB", (SIZE, SIZE), (4, 12, 35))
p = im.load()
for y in range(SIZE):
    for x in range(SIZE):
        glow = max(0.0, 1.0 - math.sqrt((x-250)**2 + (y-245)**2)/430)
        p[x,y] = (int(4+4*glow), int(10+34*glow), int(35+80*glow))
d = ImageDraw.Draw(im)
# Neon rounded frame
for inset, color, width in [(8,(0,220,255),12),(22,(0,115,255),4),(31,(35,255,90),5)]:
    d.rounded_rectangle((inset,inset,SIZE-inset,SIZE-inset), radius=74, outline=color, width=width)
# stars
for x,y,r in [(72,75,5),(420,80,7),(460,185,4),(50,270,4),(390,300,3),(310,55,3),(110,190,3)]:
    d.ellipse((x-r,y-r,x+r,y+r), fill=(220,250,255))
# Floating voxel island
d.polygon([(72,238),(150,190),(362,190),(440,238),(395,332),(116,332)], fill=(104,58,34), outline=(15,30,25))
d.polygon([(72,238),(150,190),(362,190),(440,238),(395,270),(116,270)], fill=(45,180,30), outline=(120,255,55))
# blocky grass pixels
for x in range(100,420,28):
    d.rectangle((x,215+(x%3)*4,x+20,234+(x%3)*4), fill=(75+(x%2)*30,220,35))
# Ore cubes
for x,y,ore in [(80,130,(0,220,255)),(375,135,(255,190,25))]:
    d.polygon([(x,y+20),(x+48,y),(x+94,y+25),(x+45,y+52)], fill=(110,125,145), outline=(225,245,255))
    d.polygon([(x,y+20),(x+45,y+52),(x+45,y+105),(x,y+73)], fill=(65,78,100), outline=(180,205,230))
    d.polygon([(x+45,y+52),(x+94,y+25),(x+94,y+78),(x+45,y+105)], fill=(85,100,120), outline=(190,215,235))
    for ox,oy in [(20,31),(55,25),(30,60),(64,70)]:
        d.rectangle((x+ox,y+oy,x+ox+12,y+oy+10), fill=ore)
# Pickaxe, chunky pixel silhouette
d.line([(195,230),(310,90),(390,120),(350,155),(315,130),(230,250)], fill=(18,30,50), width=34)
d.line([(195,230),(310,90),(390,120),(350,155),(315,130),(230,250)], fill=(0,225,255), width=21)
d.line([(210,245),(330,115)], fill=(135,75,35), width=19)
# Title
try:
    font1 = ImageFont.truetype("DejaVuSans-Bold.ttf", 65)
    font2 = ImageFont.truetype("DejaVuSans-Bold.ttf", 76)
except OSError:
    font1 = font2 = ImageFont.load_default()
def centered(text, y, font, fill, stroke=(0,0,0), sw=7):
    box = d.textbbox((0,0), text, font=font, stroke_width=sw)
    x = (SIZE-(box[2]-box[0]))//2
    d.text((x,y), text, font=font, fill=fill, stroke_width=sw, stroke_fill=stroke)
centered("MINING", 310, font1, (235,245,255), (5,18,40), 9)
centered("GAME", 385, font2, (80,255,30), (0,35,10), 10)
OUT.parent.mkdir(parents=True, exist_ok=True)
im.save(OUT, format="ICO", sizes=[(256,256),(128,128),(64,64),(48,48),(32,32),(16,16)])
print(f"Generated {OUT}")
