# 竖向样本回归

从仓库根目录运行 `dotnet run --project tools/ScrollingChecks -c Release`。默认覆盖位移、往返、无重复行、固定栏、待修正帧、工程往返以及实际 WPF 接缝修改／撤销／重做。

`Edge` 是 Edge 144.0.3719.82 在 2026-11-18 渲染的本地 HTML 样本。三张视口通过 CSS 将文章分别平移 0、150、300px，固定导航保持不动，匹配和拼接排除顶部 40px。它验证真实浏览器字体／页面像素，**不是桌面滚轮或真实应用动态滚动验收**。重建：

```powershell
dotnet run --project tools/ScrollingChecks -c Release -- --browser-sample 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
```

生成场景 `browser-article`、`electron-chat`、`office-pages` 同样不是对应应用实测。真实兼容样本应从实际竖向长截图保存 `.ssscroll`，人工比对原始帧并修正接缝后导出：

```powershell
dotnet run --project tools/ScrollingChecks -c Release -- --export-sample 'capture.ssscroll' 'Word' '具体版本' '分页文档，150% DPI，慢速竖向滚动'
dotnet run --project tools/ScrollingChecks -c Release -- --samples '导出的样本目录'
```

产物位于 `.artifacts/scrolling-checks/<GUID>`。导出保留原帧、实际参数和原自动匹配可靠标记；人工修正不会把自动匹配失败改写成成功。提交前独立核对 `ExpectedOffsets`，不要以算法自己的输出充当真值。可设置 `ExpectedImage` 为人工确认的完整输出 PNG，重放逐像素比较（容许单通道一级舍入）。不要提交私人聊天、业务文档或凭据。

JSON 必填字段：`Application`、`Version`、`Scenario`、`Frames`（按顺序的 PNG 相对路径）、`ExpectedOffsets`（人工标定的相邻竖向有符号位移）、`ExpectedReliable`（期望自动算法是否可靠）、`Trim`（每帧固定栏裁边），以及可空的 `ExpectedImage`。重放检查所有相邻匹配；实际采集链路在首个不确定帧结束，保留该帧和零位移待修正状态，并验证工程往返。

实机矩阵仍需补充：

| 应用 | 场景 | 核对 |
|---|---|---|
| Edge / Chrome / Firefox | 正文、固定导航、延迟加载图片、动画 | 文段不缺失、不重复，布局变化时拒绝不确定接缝并保留原帧 |
| VS Code / Teams 等 Electron | 代码行、聊天虚拟列表、向上回滚 | 行号／消息顺序、动态重绘、停止后可修正 |
| Word / Excel | 分页文档、冻结表头、不同缩放 | 页边界、行号、重复表头裁除、拼接像素和高度 |

每项记录应用版本、DPI、内容来源、滚动步长、固定栏裁边、自动与手动结果，并保留工程和预期 PNG。横向不在矩阵内。只有完成桌面实际滚轮采集并审核原帧才能记为真实应用通过。
