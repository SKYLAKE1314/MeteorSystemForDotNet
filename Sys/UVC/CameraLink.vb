Imports System.Threading
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Public Class CameraLink

    Public Event FrameArrived As Action(Of String, BitmapSource)

    Private _capture As VideoCapture
    Private _thread As Thread
    Private _running As Boolean
    Private _deviceId As String

    ' 只有 LoopCapture 所在的執行緒可以建立/讀取/釋放 _capture，
    ' 避免 StopCamera（呼叫端執行緒）與 LoopCapture（背景執行緒）同時存取同一顆
    ' VideoCapture 原生物件而造成的競爭條件（這正是「套用分辨率卡死閃退」的根因：
    ' 舊實作在 Join(500) 逾時後仍會直接 Dispose _capture，若背景執行緒當下正卡在
    ' 阻塞式的 _capture.Read() 呼叫中，兩個執行緒同時碰觸同一原生資源即會導致
    ' Access Violation / 閃退，或讓兩者互相卡住）。

    Public Sub New(deviceId As String)
        _deviceId = deviceId
    End Sub

    Public Sub StartCamera(deviceId As String)

        Dim index = CameraManager.FindIndexByDeviceId(deviceId)
        If index < 0 Then
            Logger.Warn($"[CameraLink] 找不到裝置索引: {deviceId}")
            Exit Sub
        End If

        _running = True

        _thread = New Thread(Sub() LoopCapture(index))
        _thread.IsBackground = True
        _thread.Start()

    End Sub

    Private Sub LoopCapture(index As Integer)

        Dim mat As New Mat()

        Try
            ' 先嘗試 DSHOW；DSHOW 開啟失敗時回退到預設 API
            _capture = New VideoCapture(index, VideoCaptureAPIs.DSHOW)

            ' 若 DSHOW 未開啟，改用預設 API 再試
            If Not _capture.IsOpened() Then
                Logger.Warn($"[CameraLink] DSHOW 開啟失敗，改用預設 API (index={index})")
                _capture.Dispose()
                _capture = New VideoCapture(index)
            End If

            If Not _capture.IsOpened() Then
                Logger.Error($"[CameraLink] 無法開啟相機: {_deviceId}")
                _capture.Dispose()
                _capture = Nothing
                Return
            End If

            ' 只有在相機確實開啟後才設定分辨率，避免 ExecutionEngineException
            Dim res = CameraSettingsHelper.GetCamResolutionByDeviceId(_deviceId)
            If res IsNot Nothing Then
                _capture.Set(VideoCaptureProperties.FrameWidth, res.Width)
                _capture.Set(VideoCaptureProperties.FrameHeight, res.Height)
                Logger.Info($"[CameraLink] {_deviceId} 設定分辨率 {res.Width}x{res.Height}")
            End If

            While _running

                Try

                    If _capture Is Nothing OrElse Not _capture.IsOpened() Then
                        Thread.Sleep(100)
                        Continue While
                    End If

                    If Not _capture.Read(mat) OrElse mat.Empty() Then Continue While

                    Dim bmp = BitmapSourceConverter.ToBitmapSource(mat)
                    bmp.Freeze()

                    RaiseEvent FrameArrived(_deviceId, bmp)

                Catch ex As Exception
                    Logger.Error($"[CameraLink] LoopCapture 異常: {ex.Message}")
                    Thread.Sleep(100)
                End Try

                Thread.Sleep(30)

            End While

        Catch ex As Exception
            Logger.Error($"[CameraLink] LoopCapture 初始化異常: {ex.Message}")
        Finally
            ' 釋放/處置永遠只在本執行緒進行，StopCamera 不再直接碰觸 _capture，
            ' 徹底消除跨執行緒同時存取同一原生資源的競爭條件。
            Try
                _capture?.Release()
                _capture?.Dispose()
            Catch
            End Try
            _capture = Nothing
            mat?.Dispose()
        End Try

    End Sub

    Public Sub StopCamera()

        _running = False

        Try
            ' 給予足夠時間讓 LoopCapture 自然離開迴圈並完成 _capture 的釋放；
            ' 逾時也「不」由本執行緒代為 Dispose（避免競爭條件），僅記錄警告。
            Dim stopped = _thread?.Join(2000)
            If stopped = False Then
                Logger.Warn($"[CameraLink] {_deviceId} 停止逾時（背景執行緒可能卡在讀取影像），已放棄等待，資源將於執行緒結束後自行釋放")
            End If
        Catch
        End Try

    End Sub

End Class
