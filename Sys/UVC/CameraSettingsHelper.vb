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

    ''' <summary>
    ''' 取得指定相機索引的已儲存分辨率，若未設定則返回 Nothing
    ''' </summary>
    Public Function GetCamResolution(index As Integer) As CameraResolutionOption
        Try
            Dim resolutions = My.Settings.CameraResolutions
            If resolutions Is Nothing OrElse resolutions.Count <= index Then Return Nothing
            Return CameraResolutionOption.FromTag(resolutions(index))
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 依相機 DeviceId 取得已儲存分辨率
    ''' </summary>
    Public Function GetCamResolutionByDeviceId(deviceId As String) As CameraResolutionOption
        Try
            Dim ids = My.Settings.CameraDeviceIds
            If ids Is Nothing Then Return Nothing
            For i = 0 To ids.Count - 1
                If CameraManager.IsSameDevice(ids(i), deviceId) Then
                    Return GetCamResolution(i)
                End If
            Next
            Return Nothing
        Catch
            Return Nothing
        End Try
    End Function

End Module
