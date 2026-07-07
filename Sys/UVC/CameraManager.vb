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

    ' =================================================================
    ' 【核心修復】取得相機清單（加入 WMI 安全防卡死防禦機制）
    ' =================================================================
    ' =================================================================
    ' 【核心修復】取得相機清單（精確對接 DsDeviceInfo 型態）
    ' =================================================================
    Public Shared Function GetCameras() As List(Of CameraInfo)

        Dim result As New List(Of CameraInfo)
        Dim wmiDevices As New List(Of CameraInfo)

        Try
            ' ── WMI 防卡死安全設定 ──────────────────────────────
            Dim options As New EnumerationOptions With {
                .ReturnImmediately = True,
                .Rewindable = False,
                .DirectRead = True,
                .Timeout = New TimeSpan(0, 0, 5) ' 超過 5 秒未回應強行逾時，不卡死系統啟動
            }

            Dim searcher As New ManagementObjectSearcher(
                "root\CIMV2",
                "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass='Camera' OR PNPClass='Image'",
                options)

            ' 遍歷 WMI 結果
            For Each obj As ManagementObject In searcher.Get()
                Try
                    Dim nameStr = obj("Name")?.ToString()
                    Dim devIdStr = obj("PNPDeviceID")?.ToString()

                    If Not String.IsNullOrWhiteSpace(devIdStr) Then
                        wmiDevices.Add(New CameraInfo With {
                            .Name = If(String.IsNullOrWhiteSpace(nameStr), "未知相機", nameStr),
                            .DeviceId = devIdStr,
                            .Index = -1
                        })
                    End If
                Catch ex As Exception
                    System.Diagnostics.Debug.WriteLine($"[WMI Device Parse Error] {ex.Message}")
                End Try
            Next
        Catch ex As Exception
            Logger.Error($"[CameraManager] WMI 查詢發生嚴重錯誤或逾時: {ex.Message}，將直接啟用 DirectShow 列舉。")
        End Try

        ' ── DirectShow：精確對接您的 DirectShowDeviceEnumerator.DsDeviceInfo ──
        ' 【核心修復】型態變更為 DirectShowDeviceEnumerator.DsDeviceInfo 解決編譯錯誤
        Dim dsDevices As List(Of DirectShowDeviceEnumerator.DsDeviceInfo) = Nothing
        Try
            dsDevices = DirectShowDeviceEnumerator.GetDevices()
        Catch ex As Exception
            Logger.Error($"[CameraManager] DirectShow 列舉發生致命異常: {ex.Message}")
            dsDevices = New List(Of DirectShowDeviceEnumerator.DsDeviceInfo)()
        End Try

        If dsDevices Is Nothing OrElse dsDevices.Count = 0 Then
            Logger.Warn("[CameraManager] DirectShow 未列舉到任何視訊裝置，改用 OpenCV 探測作為備援")
            Return GetCamerasByProbing(wmiDevices)
        End If

        Dim usedWmi As New HashSet(Of CameraInfo)

        For Each ds In dsDevices
            Dim dsInstanceId = DirectShowDeviceEnumerator.ExtractInstanceId(ds.DevicePath)
            Dim dsKey = DirectShowDeviceEnumerator.NormalizeForMatch(ds.DevicePath)

            ' 優先以「裝置實例序號」精確比對
            Dim matched = wmiDevices.FirstOrDefault(
                Function(w) Not usedWmi.Contains(w) AndAlso
                            Not String.IsNullOrWhiteSpace(dsInstanceId) AndAlso
                            String.Equals(DirectShowDeviceEnumerator.ExtractInstanceId(w.DeviceId), dsInstanceId, StringComparison.OrdinalIgnoreCase))

            ' 退而求其次：實例序號比對不到時，才用寬鬆的整串包含比對
            If matched Is Nothing Then
                matched = wmiDevices.FirstOrDefault(
                    Function(w) Not usedWmi.Contains(w) AndAlso
                                Not String.IsNullOrWhiteSpace(dsKey) AndAlso
                                Not String.IsNullOrWhiteSpace(w.DeviceId) AndAlso
                                (DirectShowDeviceEnumerator.NormalizeForMatch(w.DeviceId).Contains(dsKey) OrElse
                                 dsKey.Contains(DirectShowDeviceEnumerator.NormalizeForMatch(w.DeviceId))))

                If matched IsNot Nothing Then
                    Logger.Warn($"[CameraManager] 裝置 index={ds.Index} ({ds.Name}) 未找到精確的實例序號配對，改用寬鬆比對")
                End If
            End If

            If matched IsNot Nothing Then
                usedWmi.Add(matched)
                matched.Index = ds.Index
                If String.IsNullOrWhiteSpace(matched.Name) Then matched.Name = ds.Name
                result.Add(matched)
            Else
                ' WMI 找不到對應資料時，仍以 DirectShow 資訊建立一筆記錄
                Logger.Warn($"[CameraManager] DirectShow 裝置 index={ds.Index} ({ds.Name}) 找不到對應的 WMI 記錄，改用 DevicePath 作為唯一碼")
                result.Add(New CameraInfo With {
                    .Name = ds.Name,
                    .DeviceId = If(Not String.IsNullOrWhiteSpace(ds.DevicePath), ds.DevicePath, $"DSHOW_INDEX_{ds.Index}"),
                    .Index = ds.Index
                })
            End If
        Next

        ' 依 Index 排序，確保清單順序穩定
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