Imports System.Threading
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Public Class CameraLink

    Public Event FrameArrived As Action(Of String, BitmapSource)

    Private _capture As VideoCapture
    Private _thread As Thread
    Private _running As Boolean
    Private _deviceId As String

    Public Sub New(deviceId As String)
        _deviceId = deviceId
    End Sub

    Public Sub StartCamera(deviceId As String)

        Dim index = CameraManager.FindIndexByDeviceId(deviceId)
        If index < 0 Then
            Logger.Warn($"[CameraLink] 找不到裝置索引: {deviceId}")
            Exit Sub
        End If

        ' 先嘗試 DSHOW；DSHOW 開啟失敗時回退到預設 API
        _capture = New VideoCapture(index, VideoCaptureAPIs.DSHOW)

        ' 若 DSHOW 未開啟，改用預設 API 再試
        If Not _capture.IsOpened() Then
            Logger.Warn($"[CameraLink] DSHOW 開啟失敗，改用預設 API (index={index})")
            _capture.Dispose()
            _capture = New VideoCapture(index)
        End If

        If Not _capture.IsOpened() Then
            Logger.Error($"[CameraLink] 無法開啟相機: {deviceId}")
            _capture.Dispose()
            _capture = Nothing
            Exit Sub
        End If

        ' 只有在相機確實開啟後才設定分辨率，避免 ExecutionEngineException
        Dim res = CameraSettingsHelper.GetCamResolutionByDeviceId(deviceId)
        If res IsNot Nothing Then
            _capture.Set(VideoCaptureProperties.FrameWidth, res.Width)
            _capture.Set(VideoCaptureProperties.FrameHeight, res.Height)
            Logger.Info($"[CameraLink] {deviceId} 設定分辨率 {res.Width}x{res.Height}")
        End If

        _running = True

        _thread = New Thread(AddressOf LoopCapture)
        _thread.IsBackground = True
        _thread.Start()

    End Sub

    Private Sub LoopCapture()

        Dim mat As New Mat()

        While _running

            Try

                If _capture Is Nothing OrElse Not _capture.IsOpened() Then
                    System.Threading.Thread.Sleep(100)
                    Continue While
                End If

                If Not _capture.Read(mat) OrElse mat.Empty() Then Continue While

                Dim bmp = BitmapSourceConverter.ToBitmapSource(mat)
                bmp.Freeze()

                RaiseEvent FrameArrived(_deviceId, bmp)

            Catch ex As Exception
                Logger.Error($"[CameraLink] LoopCapture 異常: {ex.Message}")
                System.Threading.Thread.Sleep(100)
            End Try

            System.Threading.Thread.Sleep(30)

        End While

    End Sub

    Public Sub StopCamera()

        _running = False

        Try
            _thread?.Join(500)
        Catch
        End Try

        _capture?.Release()
        _capture?.Dispose()

    End Sub

End Class