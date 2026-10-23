"""从固定版本的 Material Design Icons 更新播放器资源；正常构建无需联网。"""
from pathlib import Path
from urllib.request import urlopen
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
COMMIT = "9e04201d4557e729822fb57f62a316c3dea1d4a8"
NAMES = ["skip-previous", "rewind", "step-backward", "play", "pause",
         "step-forward", "fast-forward", "skip-next", "help-circle-outline",
         "magnify-plus-outline", "magnify-minus-outline", "window-maximize", "window-restore"]
DEST = ROOT / "src/Captura/Images/PlayerIcons"
DEST.mkdir(parents=True, exist_ok=True)
resources = ['<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
             ' xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
             '<!-- 从官方 SVG 原样提取路径；来源和许可见 Images/PlayerIcons。 -->']
for name in NAMES:
    svg = urlopen(f"https://raw.githubusercontent.com/Templarian/MaterialDesign-SVG/{COMMIT}/svg/{name}.svg").read()
    (DEST / f"{name}.svg").write_bytes(svg)
    paths = ET.fromstring(svg).findall("{http://www.w3.org/2000/svg}path")
    assert len(paths) == 1, "图标结构改变，请检查转换"
    resources.append(f'<Geometry x:Key="PlayerIcon-{name}">{paths[0].attrib["d"]}</Geometry>')
resources.append('</ResourceDictionary>')
(ROOT / "src/Captura/Controls/PlayerIcons.xaml").write_text("\n".join(resources) + "\n", encoding="utf-8")
(DEST / "LICENSE.txt").write_bytes(urlopen(f"https://raw.githubusercontent.com/Templarian/MaterialDesign-SVG/{COMMIT}/LICENSE").read())
print("已更新官方播放器图标和许可；正常编译直接使用本地资源。")
