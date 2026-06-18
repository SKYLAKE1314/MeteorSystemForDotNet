Imports System.Threading
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Public Class CameraLink

    Public Event FrameArrived As Action(Of BitmapSource)

    Private _capture As VideoCapture
    Private _thread As Thread
    Private _running As Boolean

    Public Sub StartCamera()

        If _running Then Return

        Dim index As Integer = CameraManager.FindIndexByDeviceId(My.Settings.CameraDeviceId)

        If index < 0 Then
            ErrorDialogHelper.ShowError("Camera not found")
            Return
        End If

        _capture = New VideoCapture(index, VideoCaptureAPIs.DSHOW)
        _capture.Set(VideoCaptureProperties.FrameWidth, 3840)
        _capture.Set(VideoCaptureProperties.FrameHeight, 2880)
        _capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC("M", "J", "P", "G"))
        '_capture.Set(VideoCaptureProperties.Fps, 30)

        Dim w = _capture.Get(VideoCaptureProperties.FrameWidth)
        Dim h = _capture.Get(VideoCaptureProperties.FrameHeight)

        Logger.Debug($"Actual resolution: {w} x {h}")

        If Not _capture.IsOpened() Then
            ErrorDialogHelper.ShowError("Camera open failed")
            Return
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
                Dim ok = _capture.Read(mat)

                If Not ok OrElse mat Is Nothing OrElse mat.Empty() Then
                    Logger.Debug("Frame lost")
                    Continue While
                End If

                _capture.Read(mat)

                If mat Is Nothing OrElse mat.Empty() Then Continue While

                Dim bmp = BitmapSourceConverter.ToBitmapSource(mat)
                bmp.Freeze()

                RaiseEvent FrameArrived(bmp)

            Catch
                Exit While
            End Try

            Thread.Sleep(30)

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
        _capture = Nothing

    End Sub

End Class