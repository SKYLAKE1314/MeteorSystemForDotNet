Public Class CameraInfo

    Public Property Name As String

    Public Property DeviceId As String

    Public Property Index As Integer

    Public Overrides Function ToString() As String
        Return Name
    End Function

End Class