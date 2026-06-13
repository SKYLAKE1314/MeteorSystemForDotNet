Public Class CameraService

    Private Shared ReadOnly _instance As New CameraService()

    Public Shared ReadOnly Property Instance As CameraService
        Get
            Return _instance
        End Get
    End Property
    ' 存最新一幀影像，供UI顯示用
    Public Property LatestFrame As BitmapSource

    Private _camera As New CameraLink()

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return _running
        End Get
    End Property

    Public Event FrameArrived(bitmap As BitmapSource)

    Private _running As Boolean

    Private Sub New()

        AddHandler _camera.FrameArrived,
    Sub(img)

        LatestFrame = img
        RaiseEvent FrameArrived(img)

    End Sub

    End Sub

    Public Sub Start()

        If _running Then Return

        _camera.StartCamera()

        _running = True

    End Sub

    Public Sub [Stop]()

        If Not _running Then Return

        _camera.StopCamera()

        _running = False

    End Sub

End Class