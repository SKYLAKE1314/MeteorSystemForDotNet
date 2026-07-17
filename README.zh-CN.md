# MetroSystemForDotNet

[繁體中文](README.md) | [简体中文](README.zh-CN.md)

---

本项目基于 **Apache License 2.0** 授权条款发布。

您在遵循 Apache-2.0 License 条款的前提下，才可使用、修订与再发行本项目。
欢迎访问我的网页 http://skylake.zh.kg/

---

# Introduction
基于 .NET 10 WPF 架构开发的机器视觉检测平台，整合图像采集、实时图像处理、OCR 识别、条码扫描、IO 控制与 WebSocket 远程通讯。

---

# Development

## 开发环境与框架需求
- **开发平台**: .NET 10 (WPF) / Windows SDK
- **主要依赖库**:
  - **OpenCvSharp4**: 负责底层图像的转换、增强与绘制。
  - **Newtonsoft.Json**: 用于 WebSocket JSON 数据解析与传输。
  - **ZXing.Net**: 提供条码与二维码解码功能。
  - **PaddleOCR**: 本地文字识别引擎。

---

## 扩展与接入其他工业相机 SDK (如海康 Hikvision / 大华 Dahua / 迈德威视 MindVision)

本平台采用极度解耦的图像总线设计。不论相机是基于 OpenCV UVC (DirectShow) 协议，还是基于工业 SDK (HikVision MVS SDK, Dahua MV SDK 等)，均可通过简单的包装接入平台的 `CameraService`。

### 步骤 1: 建立您的相机驱动包装类 (以海康 SDK 为例)
建立一个实现工业 SDK 串流回调的驱动封装。在相机的帧接收回调（Frame Callback）中，将相机的原生指针（IntPtr）或二进制数组转换为 `BitmapSource` 或 `Mat`：

```vb
Imports System.Windows.Media.Imaging
Imports OpenCvSharp

Public Class HikCameraLink
    Public Event FrameArrived As Action(Of String, BitmapSource)
    Private _deviceId As String
    Private _handle As IntPtr ' 海康相机句柄

    Public Sub New(deviceId As String)
        _deviceId = deviceId
    End Sub

    Public Sub Start()
        ' 1. 初始化海康 SDK 并开启相机
        ' 2. 注册海康相机帧回调函数
        ' MySDK.RegisterFrameCallback(_handle, AddressOf OnHikFrameCallback)
        ' 3. 开始采集串流
    End Sub

    Private Sub OnHikFrameCallback(pData As IntPtr, ByRef pFrameInfo As SDK_FRAME_OUT_INFO, pUser As IntPtr)
        ' ─── 高效图像转换示例 ───
        ' 海康原生帧像素格式通常为 Mono8 或 Bayer，需转换为 RGB/BGR
        Dim width As Integer = pFrameInfo.nWidth
        Dim height As Integer = pFrameInfo.nHeight

        ' 1. 使用 OpenCvSharp Mat 直接包装原生指针 (零拷贝)
        Using rawMat As New Mat(height, width, MatType.CV_8UC1, pData)
            Using bgrMat As New Mat()
                ' 转换为适合 WPF 显示的 BGR 格式
                Cv2.CvtColor(rawMat, bgrMat, ColorConversionCodes.BayerBG2BGR)

                ' 2. 转换为 WPF 友好的 BitmapSource
                Dim bmp As BitmapSource = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(bgrMat)
                
                ' 3. 冻结 BitmapSource 确保跨线程安全传递
                bmp.Freeze()

                ' 4. 抛出事件通知 Service
                RaiseEvent FrameArrived(_deviceId, bmp)
            End Using
        End Using
    End Sub

    Public Sub [Stop]()
        ' 1. 停止采集，注销回调
        ' 2. 关闭相机控制句柄
    End Sub
End Class
```

### 步骤 2: 在 CameraService 中注册并串接事件
在 [CameraService.vb](file:///c:/VisionTek/FranceLake_26R2/MeteorSystemForDotNet/Sys/UVC/CameraService.vb) 中，将 `_cameras` 的对象替换或扩展为您的自定义相机类，并将其 `FrameArrived` 事件引流至 `CameraService` 的广播总线上：

```vb
    ' 示例：串接 Hik 驱动到 CameraService
    Public Sub StartHikCamera(deviceId As String)
        Dim cam As New HikCameraLink(deviceId)

        AddHandler cam.FrameArrived,
            Sub(id As String, img As BitmapSource)
                SyncLock _frames
                    _frames(id) = img
                End SyncLock
                
                ' 广播给首页 UI 及 OCR/解码 后台任务
                RaiseEvent FrameArrived(id, img)
            End Sub

        cam.Start()
    End Sub
```

通过此通道，整个检测平台（包括实时预览、定位算法、条码与 OCR 推理阶段）都将无缝接收来自该工业相机的超高速即时帧！
