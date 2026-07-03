Public Class CameraInfo

    Public Property Name As String

    Public Property DeviceId As String

    Public Property Index As Integer

    ''' <summary>
    ''' 取得顯示名稱，結合相機名稱和設備識別碼的簡短版本
    ''' 格式: "Camera Name (@ABC123)" 用於區分同名相機
    ''' </summary>
    Public ReadOnly Property DisplayName As String
        Get
            If String.IsNullOrWhiteSpace(DeviceId) Then
                Return Name
            End If

            ' 從 DeviceId 提取簡短的識別碼（通常是最後幾個字符）
            ' 例如: \\?\PCI#VEN_XXXX&DEV_XXXX&SUBSYS_XXXX&REV_XX@3&2B1EBF6D&0&79 -> @79
            Dim shortId = ExtractShortDeviceId(DeviceId)

            If String.IsNullOrWhiteSpace(Name) Then
                Return $"相機 ({shortId})"
            Else
                Return $"{Name} ({shortId})"
            End If
        End Get
    End Property

    ''' <summary>
    ''' 從完整的 WMI 設備識別碼提取簡短版本
    ''' </summary>
    Private Function ExtractShortDeviceId(deviceId As String) As String
        Try
            ' 通常 PNPDeviceID 的格式是: ACPI\PNP0A08\0 或 USB\VID_XXXX&PID_XXXX\SERIALNUMBER@N
            ' 提取最後的唯一標識部分（通常在 @ 符號之後或最後的數字段）
            If deviceId.Contains("@") Then
                Dim parts = deviceId.Split("@"c)
                Return "@" & parts(parts.Length - 1)
            ElseIf deviceId.Contains("\") Then
                Dim parts = deviceId.Split("\"c)
                Return parts(parts.Length - 1)
            Else
                ' 如果格式不符合，返回最後 6 個字符
                Return If(deviceId.Length > 6, deviceId.Substring(deviceId.Length - 6), deviceId)
            End If
        Catch
            Return deviceId.Substring(Math.Max(0, deviceId.Length - 6))
        End Try
    End Function

    Public Overrides Function ToString() As String
        Return DisplayName
    End Function

End Class