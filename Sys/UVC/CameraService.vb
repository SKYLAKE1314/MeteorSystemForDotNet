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

    Private Function ResolveCanonicalDeviceId(deviceId As String) As String
        If String.IsNullOrWhiteSpace(deviceId) Then Return deviceId
        Dim cached = CameraManager.GetCachedCameras()
        If cached IsNot Nothing Then
            Dim cam = cached.FirstOrDefault(Function(c) CameraManager.IsSameDevice(c.DeviceId, deviceId))
            If cam IsNot Nothing Then
                Return cam.DeviceId
            End If
        End If
        Return deviceId
    End Function

    ' =========================
    ' 啟動指定相機
    ' =========================
    Public Sub StartCamera(deviceId As String)
        If String.IsNullOrWhiteSpace(deviceId) Then Return
        Dim canonicalId = ResolveCanonicalDeviceId(deviceId)

        SyncLock _cameras
            ' 如果已經在執行中，不重複啟動
            If _cameras.ContainsKey(canonicalId) Then Return

            Dim cam As New CameraLink(canonicalId)

            AddHandler cam.FrameArrived,
                Sub(id As String, img As BitmapSource)
                    SyncLock _frames
                        _frames(id) = img
                    End SyncLock
                    RaiseEvent FrameArrived(id, img)
                End Sub

            cam.StartCamera(canonicalId)
            _cameras(canonicalId) = cam
        End SyncLock
    End Sub

    Public Sub StopCamera(deviceId As String)
        Dim canonicalId = ResolveCanonicalDeviceId(deviceId)
        Dim cam As CameraLink = Nothing

        SyncLock _cameras
            If Not _cameras.TryGetValue(canonicalId, cam) Then Return
            _cameras.Remove(canonicalId)
        End SyncLock

        If cam IsNot Nothing Then
            cam.StopCamera()
        End If
    End Sub

    Public Function IsRunning(deviceId As String) As Boolean
        If String.IsNullOrWhiteSpace(deviceId) Then Return False
        Dim canonicalId = ResolveCanonicalDeviceId(deviceId)
        SyncLock _cameras
            Return _cameras.ContainsKey(canonicalId)
        End SyncLock
    End Function

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
        Dim canonicalId = ResolveCanonicalDeviceId(deviceId)

        SyncLock _frames
            If _frames.ContainsKey(canonicalId) Then
                Return _frames(canonicalId)
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