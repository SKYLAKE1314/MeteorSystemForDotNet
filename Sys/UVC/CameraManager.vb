Imports System.Management
Imports OpenCvSharp

Public Class CameraManager

    Public Shared Event CameraChanged As Action

    Private Shared _cameraCache As List(Of CameraInfo)
    Private Shared _initialized As Boolean = False

    Public Shared Sub Initialize()

        If _initialized Then Return
        _initialized = True

        _cameraCache = GetCameras()

    End Sub

    Public Shared Sub NotifyCameraChanged()
        RaiseEvent CameraChanged()
    End Sub

    Public Shared Function GetCameras() As List(Of CameraInfo)

        Dim result As New List(Of CameraInfo)

        ' ── WMI：取得每個相機裝置的唯一 PNPDeviceID ──────────────
        Dim searcher As New ManagementObjectSearcher(
            "SELECT * FROM Win32_PnPEntity WHERE PNPClass='Camera' OR PNPClass='Image'")

        Dim wmiDevices As New List(Of CameraInfo)

        For Each obj As ManagementObject In searcher.Get()
            wmiDevices.Add(New CameraInfo With {
                .Name = obj("Name")?.ToString(),
                .DeviceId = obj("PNPDeviceID")?.ToString(),
                .Index = -1
            })
        Next

        ' ── DirectShow：取得與 OpenCvSharp(DSHOW) index 順序完全一致的裝置清單 ──
        ' 每個裝置附帶 DevicePath，與 WMI PNPDeviceID 內含相同的 VID/PID/序號片段，
        ' 可精確配對，取代舊有「依猜測順序對應」的錯誤作法（那正是相機錯亂/串線的根因）。
        Dim dsDevices = DirectShowDeviceEnumerator.GetDevices()

        If dsDevices.Count = 0 Then
            Logger.Warn("[CameraManager] DirectShow 未列舉到任何視訊裝置，改用 OpenCV 探測作為備援")
            Return GetCamerasByProbing(wmiDevices)
        End If

        Dim usedWmi As New HashSet(Of CameraInfo)

        For Each ds In dsDevices

            Dim dsInstanceId = DirectShowDeviceEnumerator.ExtractInstanceId(ds.DevicePath)
            Dim dsKey = DirectShowDeviceEnumerator.NormalizeForMatch(ds.DevicePath)

            ' 優先以「裝置實例序號」精確比對——即使兩台相機是相同型號（VID/PID 相同），
            ' 實例序號仍是唯一的，可避免舊有「整串 Contains 雙向比對」在同型號多相機時的誤配對。
            Dim matched = wmiDevices.FirstOrDefault(
                Function(w) Not usedWmi.Contains(w) AndAlso
                            Not String.IsNullOrWhiteSpace(dsInstanceId) AndAlso
                            String.Equals(DirectShowDeviceEnumerator.ExtractInstanceId(w.DeviceId), dsInstanceId, StringComparison.OrdinalIgnoreCase))

            ' 退而求其次：實例序號比對不到時，才用寬鬆的整串包含比對（僅作為最後防線）
            If matched Is Nothing Then
                matched = wmiDevices.FirstOrDefault(
                    Function(w) Not usedWmi.Contains(w) AndAlso
                                Not String.IsNullOrWhiteSpace(dsKey) AndAlso
                                Not String.IsNullOrWhiteSpace(w.DeviceId) AndAlso
                                (DirectShowDeviceEnumerator.NormalizeForMatch(w.DeviceId).Contains(dsKey) OrElse
                                 dsKey.Contains(DirectShowDeviceEnumerator.NormalizeForMatch(w.DeviceId))))

                If matched IsNot Nothing Then
                    Logger.Warn($"[CameraManager] 裝置 index={ds.Index} ({ds.Name}) 未找到精確的實例序號配對，改用寬鬆比對（可能不準確）")
                End If
            End If

            If matched IsNot Nothing Then
                usedWmi.Add(matched)
                matched.Index = ds.Index
                If String.IsNullOrWhiteSpace(matched.Name) Then matched.Name = ds.Name
                result.Add(matched)
            Else
                ' WMI 找不到對應資料時，仍以 DirectShow 資訊建立一筆記錄（DeviceId 用 DevicePath 頂替，確保唯一）
                Logger.Warn($"[CameraManager] DirectShow 裝置 index={ds.Index} ({ds.Name}) 找不到對應的 WMI 記錄，改用 DevicePath 作為唯一碼")
                result.Add(New CameraInfo With {
                    .Name = ds.Name,
                    .DeviceId = If(Not String.IsNullOrWhiteSpace(ds.DevicePath), ds.DevicePath, $"DSHOW_INDEX_{ds.Index}"),
                    .Index = ds.Index
                })
            End If

        Next

        ' 依 Index 排序，確保清單順序穩定、可預期
        Return result.OrderBy(Function(c) c.Index).ToList()

    End Function

    ''' <summary>
    ''' 備援方案：當 DirectShow COM 列舉失敗（例如環境不支援）時，
    ''' 退回舊的「依序探測 OpenCV index」方式，至少確保相機清單不會完全空白。
    ''' 注意：此方式在多相機同名情況下仍可能對應錯誤，僅作最後防線。
    ''' </summary>
    Private Shared Function GetCamerasByProbing(wmiDevices As List(Of CameraInfo)) As List(Of CameraInfo)
        Dim result As New List(Of CameraInfo)
        Dim openIndex As Integer = 0
        Dim unmapped As New Queue(Of CameraInfo)(wmiDevices)

        While unmapped.Count > 0
            Try
                Using cap As New VideoCapture(openIndex, VideoCaptureAPIs.DSHOW)
                    If cap.IsOpened() Then
                        Dim cam = unmapped.Dequeue()
                        cam.Index = openIndex
                        result.Add(cam)
                    Else
                        Exit While
                    End If
                End Using
            Catch ex As Exception
                Logger.Warn($"[CameraManager] index {openIndex} probe failed: {ex.Message}")
                Exit While
            End Try
            openIndex += 1
        End While

        Return result
    End Function

    Public Shared Function FindIndexByDeviceId(deviceId As String) As Integer

        If _cameraCache Is Nothing Then Return -1

        Dim cam = _cameraCache.FirstOrDefault(Function(x)
                                                  Return String.Equals(x.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                                              End Function)

        If cam Is Nothing Then Return -1

        Return cam.Index

    End Function

    Public Shared Function GetCachedCameras() As List(Of CameraInfo)
        Return If(_cameraCache, New List(Of CameraInfo))
    End Function

    Public Shared Sub Refresh()
        _cameraCache = GetCameras()
        For Each cam In _cameraCache
            Logger.Info($"Camera Found: {cam.Name} | {cam.DeviceId} | Index:{cam.Index}")
        Next

        NotifyCameraChanged()
    End Sub

End Class