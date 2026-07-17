# MetroSystemForDotNet

[繁體中文](README.md) | [简体中文](README.zh-CN.md)

---

本專案基於 **Apache License 2.0** 授權條款釋出。

您在遵循 Apache-2.0 License 條款的前提下，才可使用、修訂與再發行本專案。
歡迎訪問我的網頁 http://skylake.zh.kg/

---

# Introduction
基於 .NET 10 WPF 架構開發的機器視覺檢測平台，整合影像擷取、即時影像處理、OCR 辨識、條碼掃描、IO 控制與 WebSocket 遠端通訊。

---

# Development

## 開發環境與框架需求
- **開發平台**: .NET 10 (WPF) / Windows SDK
- **主要依賴庫**:
  - **OpenCvSharp4**: 負責底層影像的轉換、增強與繪製。
  - **Newtonsoft.Json**: 用於 WebSocket JSON 數據解析與傳輸。
  - **ZXing.Net**: 提供條碼與二維碼解碼功能。
  - **PaddleOCR**: 本地文字識別引擎。

---

## 擴充與接入其他工業相機 SDK (如海康 Hikvision / 大華 Dahua / 邁德威視 MindVision)

本平台採用極度解耦的影像總線設計。不論相機是基於 OpenCV UVC (DirectShow) 協議，還是基於工業 SDK (HikVision MVS SDK, Dahua MV SDK 等)，均可透過簡單的包裝接入平台的 `CameraService`。

### 步驟 1: 建立您的相機驅動包裝類別 (以海康 SDK 為例)
建立一個實作工業 SDK 串流回調的驅動封裝。在相機的幀接收回調（Frame Callback）中，將相機的原生指針（IntPtr）或二進制陣列轉換為 `BitmapSource` 或 `Mat`：

```vb
Imports System.Windows.Media.Imaging
Imports OpenCvSharp

Public Class HikCameraLink
    Public Event FrameArrived As Action(Of String, BitmapSource)
    Private _deviceId As String
    Private _handle As IntPtr ' 海康相機句柄

    Public Sub New(deviceId As String)
        _deviceId = deviceId
    End Sub

    Public Sub Start()
        ' 1. 初始化海康 SDK 並開啟相機
        ' 2. 註冊海康相機幀回調函數
        ' MySDK.RegisterFrameCallback(_handle, AddressOf OnHikFrameCallback)
        ' 3. 開始採集串流
    End Sub

    Private Sub OnHikFrameCallback(pData As IntPtr, ByRef pFrameInfo As SDK_FRAME_OUT_INFO, pUser As IntPtr)
        ' ─── 高效影像轉換示例 ───
        ' 海康原生影格像素格式通常為 Mono8 或 Bayer，需轉換為 RGB/BGR
        Dim width As Integer = pFrameInfo.nWidth
        Dim height As Integer = pFrameInfo.nHeight

        ' 1. 使用 OpenCvSharp Mat 直接包裝原生指針 (零拷貝)
        Using rawMat As New Mat(height, width, MatType.CV_8UC1, pData)
            Using bgrMat As New Mat()
                ' 轉換為適合 WPF 顯示的 BGR 格式
                Cv2.CvtColor(rawMat, bgrMat, ColorConversionCodes.BayerBG2BGR)

                ' 2. 轉換為 WPF 友好的 BitmapSource
                Dim bmp As BitmapSource = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToBitmapSource(bgrMat)
                
                ' 3. 凍結 BitmapSource 確保跨執行緒安全傳遞
                bmp.Freeze()

                ' 4. 拋出事件通知 Service
                RaiseEvent FrameArrived(_deviceId, bmp)
            End Using
        End Using
    End Sub

    Public Sub [Stop]()
        ' 1. 停止採集，註銷回調
        ' 2. 關閉相機控制控制碼
    End Sub
End Class
```

### 步驟 2: 於 `CameraService` 中註冊並串接事件
在 [CameraService.vb](file:///c:/VisionTek/FranceLake_26R2/MeteorSystemForDotNet/Sys/UVC/CameraService.vb) 中，將 `_cameras` 的對象替換或擴展為您的自訂相機類別，並將其 `FrameArrived` 事件引流至 `CameraService` 的廣播總線上：

```vb
    ' 範例：串接 Hik 驅動到 CameraService
    Public Sub StartHikCamera(deviceId As String)
        Dim cam As New HikCameraLink(deviceId)

        AddHandler cam.FrameArrived,
            Sub(id As String, img As BitmapSource)
                SyncLock _frames
                    _frames(id) = img
                End SyncLock
                
                ' 廣播給首頁 UI 及 OCR/解碼 背景任務
                RaiseEvent FrameArrived(id, img)
            End Sub

        cam.Start()
    End Sub
```

透過此管道，整個檢測平台（包括實時預覽、定位算法、條碼與 OCR 推論階段）都將無縫接收來自該工業相機的超高速即時影格！