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
            Throw New Exception("Camera not found")
        End If

        _capture = New VideoCapture(index)

        If Not _capture.IsOpened() Then
            Throw New Exception("Camera open failed")
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