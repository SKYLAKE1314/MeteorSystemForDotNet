Public Class CameraService

    Private Shared ReadOnly _inst As New CameraService()

    Public Shared ReadOnly Property Instance As CameraService
        Get
            Return _inst
        End Get
    End Property

    ' OrdinalIgnoreCase: WMI DeviceId 大小寫可能不一致，必須不分大小寫比對
    Private _cameras As New Dictionary(Of String, CameraLink)(StringComparer.OrdinalIgnoreCase)
    Private _frames As New Dictionary(Of String, BitmapSource)(StringComparer.OrdinalIgnoreCase)

    Public Event FrameArrived As Action(Of String, BitmapSource)

    Private Sub New()
    End Sub

    ' =========================
    ' 啟動指定相機
    ' =========================
    Public Sub StartCamera(deviceId As String)

        If String.IsNullOrWhiteSpace(deviceId) Then Return

        ' 如果已經在執行中，不重複啟動
        SyncLock _cameras
            If _cameras.ContainsKey(deviceId) Then Return
        End SyncLock

        Dim cam As New CameraLink(deviceId)

        AddHandler cam.FrameArrived,
            Sub(id As String, img As BitmapSource)

                SyncLock _frames
                    _frames(id) = img
                End SyncLock

                RaiseEvent FrameArrived(id, img)

            End Sub

        cam.StartCamera(deviceId)

        SyncLock _cameras
            _cameras(deviceId) = cam
        End SyncLock

    End Sub

    Public Sub StopCamera(deviceId As String)

        Dim cam As CameraLink = Nothing
        SyncLock _cameras
            If _cameras.TryGetValue(deviceId, cam) Then
                _cameras.Remove(deviceId)
            End If
        End SyncLock
        cam?.StopCamera()

        SyncLock _frames
            _frames.Remove(deviceId)
        End SyncLock

    End Sub

    ' =========================
    ' 啟動全部相機
    ' =========================
    Public Sub StartAll()

        Dim ids = My.Settings.CameraDeviceIds
        If ids Is Nothing OrElse ids.Count = 0 Then Return

        For Each id In ids
            StartCamera(id)
        Next

    End Sub

    ' =========================
    ' 取得指定相機畫面
    ' =========================
    Public Function GetFrame(deviceId As String) As BitmapSource

        SyncLock _frames
            If _frames.ContainsKey(deviceId) Then
                Return _frames(deviceId)
            End If
        End SyncLock

        Return Nothing

    End Function

    Public Sub StopAll()

        Dim cams As List(Of CameraLink)
        SyncLock _cameras
            cams = _cameras.Values.ToList()
            _cameras.Clear()
        End SyncLock

        For Each cam In cams
            cam.StopCamera()
        Next

        SyncLock _frames
            _frames.Clear()
        End SyncLock

    End Sub

End Class