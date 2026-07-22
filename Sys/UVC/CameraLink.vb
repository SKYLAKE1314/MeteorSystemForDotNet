Imports System.Threading
Imports System.Windows
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Public Class CameraLink

    Public Event FrameArrived As Action(Of String, BitmapSource)

    Private _capture As VideoCapture
    Private _thread As Thread
    Private _running As Boolean
    Private _deviceId As String
    Private ReadOnly _captureLock As New Object()

    Public Sub New(deviceId As String)
        _deviceId = deviceId
    End Sub

    Public Sub StartCamera(deviceId As String)
        Dim index = CameraManager.FindIndexByDeviceId(deviceId)
        If index < 0 Then
            Logger.Warn($"[CameraLink] 找不到裝置索引: {deviceId}")
            Exit Sub
        End If

        If _running AndAlso _thread IsNot Nothing AndAlso _thread.IsAlive Then
            Return
        End If

        _running = True

        _thread = New Thread(Sub() LoopCapture(index))
        _thread.IsBackground = True
        _thread.Start()
    End Sub

    Private Sub LoopCapture(index As Integer)
        Try
            SyncLock _captureLock
                _capture = New VideoCapture(index, VideoCaptureAPIs.DSHOW)

                If Not _capture.IsOpened() Then
                    _capture.Dispose()
                    _capture = New VideoCapture(index)
                End If

                If Not _capture.IsOpened() Then
                    Logger.Error($"[CameraLink] 無法開啟相機: {_deviceId}")
                    _capture.Dispose()
                    _capture = Nothing
                    Return
                End If

                ' 設定分辨率與格式
                Dim res = CameraSettingsHelper.GetCamResolutionByDeviceId(_deviceId)
                If res IsNot Nothing Then
                    ' 為了避免高解析度造成 USB 頻寬瓶頸 (這會導致相機傳回全黑畫面、stdDev=0)，強制使用 MJPG 壓縮格式
                    _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC("M"c, "J"c, "P"c, "G"c))

                    _capture.Set(VideoCaptureProperties.FrameWidth, res.Width)
                    _capture.Set(VideoCaptureProperties.FrameHeight, res.Height)

                    Dim actualW = CInt(_capture.Get(VideoCaptureProperties.FrameWidth))
                    Dim actualH = CInt(_capture.Get(VideoCaptureProperties.FrameHeight))
                    Logger.Info($"[CameraLink] {_deviceId} 設定分辨率 {res.Width}x{res.Height} (實際生效: {actualW}x{actualH})")
                End If
            End SyncLock

            While _running
                Try
                    Dim frameCaptured As Boolean = False
                    Dim bmp As BitmapSource = Nothing

                    SyncLock _captureLock
                        If Not _running OrElse _capture Is Nothing OrElse Not _capture.IsOpened() OrElse _capture.IsDisposed Then
                            Thread.Sleep(50)
                            Continue While
                        End If

                        Using mat As New Mat()
                            If _capture.Read(mat) AndAlso Not mat.Empty() AndAlso Not mat.IsDisposed Then
                                bmp = MatToBitmapSource(mat)
                                frameCaptured = True
                            End If
                        End Using
                    End SyncLock

                    If frameCaptured AndAlso bmp IsNot Nothing Then
                        bmp.Freeze()
                        RaiseEvent FrameArrived(_deviceId, bmp)
                    Else
                        Thread.Sleep(20)
                    End If

                Catch ex As Exception
                End Try

                Thread.Sleep(20)
            End While

        Catch ex As Exception
            Logger.Error($"[CameraLink] LoopCapture 初始化異常: {ex.Message}")
        Finally
            SyncLock _captureLock
                Try
                    _capture?.Release()
                    _capture?.Dispose()
                Catch
                End Try
                _capture = Nothing
            End SyncLock
        End Try
    End Sub

    Private Function MatToBitmapSource(mat As Mat) As BitmapSource
        If mat Is Nothing OrElse mat.IsDisposed OrElse mat.CvPtr = IntPtr.Zero Then Return Nothing
        Try
            Return BitmapSourceConverter.ToBitmapSource(mat)
        Catch ex As Exception
            Logger.Error($"[CameraLink] MatToBitmapSource 轉換異常: {ex.Message}")
            Return Nothing
        End Try
    End Function

    Public Sub StopCamera()
        _running = False
        Try
            Dim stopped = _thread?.Join(1500)
            If stopped = False Then
                Logger.Warn($"[CameraLink] {_deviceId} 停止逾時，已放棄等待")
            End If
        Catch
        End Try

        SyncLock _captureLock
            Try
                If _capture IsNot Nothing Then
                    _capture.Release()
                    _capture.Dispose()
                    _capture = Nothing
                End If
            Catch
            End Try
        End SyncLock
    End Sub

End Class