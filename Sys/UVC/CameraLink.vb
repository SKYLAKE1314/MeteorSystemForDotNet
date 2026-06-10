Imports System.Threading
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions

Public Class CameraLink

    Public Event FrameArrived(bitmap As System.Windows.Media.Imaging.BitmapSource)

    Private _capture As VideoCapture
    Private _cameraThread As Thread
    Private _running As Boolean = False

    Public Sub StartCamera()

        _capture = New VideoCapture(0)
        _running = True

        _cameraThread = New Thread(AddressOf CaptureLoop)
        _cameraThread.IsBackground = True
        _cameraThread.Start()

    End Sub

    Private Sub CaptureLoop()

        Dim frame As New Mat()

        While _running

            _capture.Read(frame)

            If Not frame.Empty() Then

                Dim bitmap = BitmapSourceConverter.ToBitmapSource(frame)
                bitmap.Freeze()

                RaiseEvent FrameArrived(bitmap)

            End If

            Thread.Sleep(10)

        End While

    End Sub

    Public Sub StopCamera()

        _running = False

        _capture?.Release()
        _capture?.Dispose()

    End Sub

End Class