Imports System.Threading
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Public Class CameraLink

    Public Event FrameArrived(bitmap As System.Windows.Media.Imaging.BitmapSource)

    Private _capture As VideoCapture
    Private _cameraThread As Thread
    Private _running As Boolean = False

    Public Sub StartCamera()

        Dim index As Integer =
        CameraManager.FindIndexByDeviceId(
            My.Settings.CameraDeviceId)

        If index < 0 Then

            Throw New Exception(
            "找不到已設定的相機")

        End If

        _capture = New VideoCapture(index)

        _running = True

        _cameraThread =
        New Thread(AddressOf CaptureLoop)

        _cameraThread.IsBackground = True
        _cameraThread.Start()

    End Sub

    Private Sub CaptureLoop()

        Dim frame As New Mat()

        While _running

            Try

                If _capture Is Nothing OrElse
               _capture.IsDisposed Then Exit While

                _capture.Read(frame)

                If frame Is Nothing OrElse frame.Empty() Then Continue While

                Dim bitmap = BitmapSourceConverter.ToBitmapSource(frame)
                bitmap.Freeze()

                RaiseEvent FrameArrived(bitmap)

            Catch ex As Exception
                Exit While ' 退出循環
            End Try

            Thread.Sleep(10)

        End While

    End Sub

    Public Sub StopCamera()

        _running = False

        If _cameraThread IsNot Nothing Then
            _cameraThread.Join(500) '等待 thread 結束
        End If

        If _capture IsNot Nothing Then
            _capture.Release()
            _capture.Dispose()
            _capture = Nothing
        End If

    End Sub

End Class