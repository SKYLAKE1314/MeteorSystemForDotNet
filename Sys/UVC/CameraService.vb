Imports System.Windows.Media.Imaging

Public Class CameraService

    Private Shared ReadOnly _instance As New CameraService()
    Public Shared ReadOnly Property Instance As CameraService
        Get
            Return _instance
        End Get
    End Property

    Private _camera As New CameraLink()
    Private _running As Boolean

    Public Event FrameArrived As Action(Of BitmapSource)
    Public Property LatestFrame As BitmapSource

    Private Sub New()
        AddHandler _camera.FrameArrived, AddressOf OnFrame
    End Sub

    Private Sub OnFrame(img As BitmapSource)

        LatestFrame = img

        RaiseEvent FrameArrived(img)

    End Sub

    Public Sub Start()
        If _running Then Return

        _camera.StartCamera()
        _running = True
    End Sub

    Public Sub [Stop]()
        _camera.StopCamera()
        _running = False
    End Sub

End Class