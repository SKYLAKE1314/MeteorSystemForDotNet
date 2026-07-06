Public Class CameraResolutionOption

    Public Property Width As Integer
    Public Property Height As Integer

    Public Sub New(w As Integer, h As Integer)
        Width = w
        Height = h
    End Sub

    Public ReadOnly Property DisplayName As String
        Get
            Return $"{Width} × {Height}"
        End Get
    End Property

    Public ReadOnly Property Tag As String
        Get
            Return $"{Width}x{Height}"
        End Get
    End Property

    ''' <summary>
    ''' 常用相機分辨率清單（由低至高）
    ''' </summary>
    Public Shared Function CommonResolutions() As List(Of CameraResolutionOption)
        Return New List(Of CameraResolutionOption) From {
            New CameraResolutionOption(640, 480),
            New CameraResolutionOption(800, 600),
            New CameraResolutionOption(1024, 768),
            New CameraResolutionOption(1280, 720),
            New CameraResolutionOption(1280, 960),
            New CameraResolutionOption(1600, 1200),
            New CameraResolutionOption(1920, 1080),
            New CameraResolutionOption(2048, 1536),
            New CameraResolutionOption(2560, 1440),
            New CameraResolutionOption(2592, 1944),
            New CameraResolutionOption(3264, 2448),
            New CameraResolutionOption(3840, 2160),
            New CameraResolutionOption(4096, 2160),
            New CameraResolutionOption(4096, 3072),
            New CameraResolutionOption(4208, 3120),
            New CameraResolutionOption(4656, 3496),
            New CameraResolutionOption(5120, 3840),
            New CameraResolutionOption(5472, 3648),
            New CameraResolutionOption(6000, 4000),
            New CameraResolutionOption(7680, 4320)
        }
    End Function

    Public Shared Function FromTag(tag As String) As CameraResolutionOption
        If String.IsNullOrWhiteSpace(tag) Then Return Nothing
        Dim parts = tag.Split("x"c)
        If parts.Length <> 2 Then Return Nothing
        Dim w, h As Integer
        If Integer.TryParse(parts(0), w) AndAlso Integer.TryParse(parts(1), h) Then
            Return New CameraResolutionOption(w, h)
        End If
        Return Nothing
    End Function

    Public Overrides Function ToString() As String
        Return DisplayName
    End Function

    Public Overrides Function Equals(obj As Object) As Boolean
        Dim other = TryCast(obj, CameraResolutionOption)
        If other Is Nothing Then Return False
        Return Width = other.Width AndAlso Height = other.Height
    End Function

    Public Overrides Function GetHashCode() As Integer
        Return (Width * 10000 + Height).GetHashCode()
    End Function

End Class
