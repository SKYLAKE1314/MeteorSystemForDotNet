Public Module CameraSettingsHelper

    ''' <summary>
    ''' 安全取得 CameraDeviceIds[index]，若不存在則返回空字串
    ''' </summary>
    Public Function GetCamId(index As Integer) As String
        Try
            Dim ids = My.Settings.CameraDeviceIds
            If ids Is Nothing OrElse ids.Count <= index Then Return ""
            Return If(ids(index), "")
        Catch
            Return ""
        End Try
    End Function

End Module
