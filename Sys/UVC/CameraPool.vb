Public Class CameraPool

    Private Shared _map As New Dictionary(Of String, CameraLink)

    Public Shared Function GetCamera(deviceId As String) As CameraLink

        If _map.ContainsKey(deviceId) Then
            Return _map(deviceId)
        End If

        Dim cam As New CameraLink(deviceId)
        cam.StartCamera(deviceId)

        _map(deviceId) = cam

        Return cam

    End Function

End Class