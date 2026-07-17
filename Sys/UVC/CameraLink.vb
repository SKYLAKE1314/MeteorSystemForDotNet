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

            ' 設定分辨率
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

                    ' 先標準讀取一幀作為安全保底畫面
                    If Not _capture.Read(mat) OrElse mat.Empty() Then
                        Thread.Sleep(25)
                        Continue While
                    End If

                    ' 快速 Grab 2 次清除緩存積壓，不進行 Retrieve 解碼
                    Dim hasNewer As Boolean = False
                    For i As Integer = 1 To 2
                        If _capture.Grab() Then
                            hasNewer = True
                        Else
                            Exit For
                        End If
                    Next

                    ' 只有最後一幀才執行 Retrieve（解碼），省去中間幀的解碼開銷！
                    If hasNewer Then
                        Dim temp As New Mat()
                        If _capture.Retrieve(temp) AndAlso Not temp.Empty() Then
                            mat.Dispose()
                            mat = temp
                        Else
                            temp.Dispose()
                        End If
                    End If

                    ' 零洩漏快速轉換
                    Dim bmp = MatToBitmapSource(mat)

                    If bmp IsNot Nothing Then
                        bmp.Freeze()
                        RaiseEvent FrameArrived(_deviceId, bmp)
                    End If

                Catch ex As Exception
                    ' 保持絕對安靜
                End Try

                Thread.Sleep(100)
            End While

        Catch ex As Exception
            Logger.Error($"[CameraLink] LoopCapture 初始化異常: {ex.Message}")
        Finally
            Try
                _capture?.Release()
                _capture?.Dispose()
            Catch
            End Try
            _capture = Nothing
            mat?.Dispose()

            ' 結束後在背景悄悄做一次完整的回收即可
            GC.Collect(2, GCCollectionMode.Forced, True, True)
        End Try
    End Sub

    Private Function MatToBitmapSource(mat As Mat) As BitmapSource
        If mat Is Nothing OrElse mat.IsDisposed OrElse mat.CvPtr = IntPtr.Zero Then Return Nothing

        Dim width As Integer = mat.Width
        Dim height As Integer = mat.Height
        Dim stride As Integer = CInt(mat.Step())

        Dim pf As PixelFormat
        Select Case mat.Channels()
            Case 1
                pf = PixelFormats.Gray8
            Case 3
                pf = PixelFormats.Bgr24
            Case 4
                pf = PixelFormats.Bgra32
            Case Else
                Return BitmapSourceConverter.ToBitmapSource(mat)
        End Select

        Return BitmapSource.Create(
            width,
            height,
            96,
            96,
            pf,
            Nothing,
            mat.Data,
            stride * height,
            stride
        )
    End Function

    Public Sub StopCamera()
        _running = False
        Try
            Dim stopped = _thread?.Join(2000)
            If stopped = False Then
                Logger.Warn($"[CameraLink] {_deviceId} 停止逾時，已放棄等待")
            End If
        Catch
        End Try
    End Sub

End Class