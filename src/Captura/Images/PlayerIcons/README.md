# 播放器图标来源

播放器使用 [Material Design Icons](https://github.com/Templarian/MaterialDesign-SVG/tree/9e04201d4557e729822fb57f62a316c3dea1d4a8) 的官方 SVG，作者为 Pictogrammers 及该项目贡献者，图标遵循 Apache 2.0 许可。

版本固定为 `9e04201d4557e729822fb57f62a316c3dea1d4a8`。`Controls/PlayerIcons.xaml` 中的 Geometry 从这些 SVG 的路径原样提取，没有手工绘制或修改图标。秒数是按钮文字标签。缩放与窗口最大化／还原图标也从同一固定版本原样提取。

`scripts/update-player-icons.py` 可以重新下载同版本资源并生成资源字典。正常编译无需联网或安装 Python。分发包保留本目录中的来源说明和许可证。
