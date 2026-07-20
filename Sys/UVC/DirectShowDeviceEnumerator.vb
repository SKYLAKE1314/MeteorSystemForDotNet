Imports System.Runtime.InteropServices

''' <summary>
''' 手動 COM Interop 呼叫 DirectShow 的系統裝置列舉器（ICreateDevEnum），
''' 取得「視訊擷取裝置」類別下的裝置清單，其列舉順序與 OpenCvSharp 使用
''' VideoCaptureAPIs.DSHOW 開啟裝置時的 index 順序一致。
'''
''' 每個裝置附帶的 DevicePath 屬性（例如：
''' \\?\usb#vid_1234&pid_5678&mi_00#6&265xxxx&0&0000#{6994ad05-93ef-11d0-a3cc-00a0c9223196}\global）
''' 與 WMI Win32_PnPEntity 的 PNPDeviceID（例如：
''' USB\VID_1234&PID_5678&MI_00\6&265xxxx&0&0000）包含相同的 VID/PID 及序號片段，
''' 可用來做「精確配對」，取代舊有「依序猜測」的錯誤作法。
''' </summary>
Public Class DirectShowDeviceEnumerator

    Public Class DsDeviceInfo
        Public Property Index As Integer
        Public Property Name As String
        Public Property DevicePath As String
    End Class

    ' ===== COM 介面宣告（僅宣告本程式需要用到的成員）=====

    <ComImport()>
    <Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")>
    Private Class CreateDevEnum
    End Class

    <ComImport()>
    <Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface ICreateDevEnum
        Function CreateClassEnumerator(ByRef pType As Guid, <Out> ByRef ppEnumMoniker As IEnumMoniker, dwFlags As Integer) As Integer
    End Interface

    <ComImport()>
    <Guid("00000102-0000-0000-C000-000000000046")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IEnumMoniker
        ' 關鍵：rgelt 必須標示 MarshalAs(LPArray)，否則 CLR 會誤用 SAFEARRAY 規則封送
        ' 這個原生 COM 陣列指標，導致記憶體/堆疊損毀，造成 ExecutionEngineException。
        ' pceltFetched 也不可用 ByRef IntPtr（那是指標的指標），celt=1 時直接傳 IntPtr.Zero 即可，
        ' 用回傳值(HRESULT: S_OK=有取到 / S_FALSE=無更多項目)判斷是否成功。
        <PreserveSig()>
        Function [Next](celt As Integer,
                         <Out(), MarshalAs(UnmanagedType.LPArray, SizeParamIndex:=0)> ByVal rgelt() As IMoniker,
                         pceltFetched As IntPtr) As Integer
        Function Skip(celt As Integer) As Integer
        Sub Reset()
        Sub Clone(<Out> ByRef ppenum As IEnumMoniker)
    End Interface

    <ComImport()>
    <Guid("0000000E-0000-0000-C000-000000000046")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IMoniker
        ' 只需要 BindToStorage 用來取得 IPropertyBag，其餘成員以佔位保留正確 vtable 順序
        Sub GetClassID(<Out> ByRef pClassID As Guid)
        Function IsDirty() As Integer
        Sub Load(pStm As IntPtr)
        Sub Save(pStm As IntPtr, fClearDirty As Boolean)
        Sub GetSizeMax(<Out> ByRef pcbSize As Long)
        Sub BindToObject(pbc As IntPtr, pmkToLeft As IntPtr, ByRef riidResult As Guid, <Out> ByRef ppvResult As Object)
        Sub BindToStorage(pbc As IntPtr, pmkToLeft As IntPtr, ByRef riid As Guid, <Out> ByRef ppvObj As Object)
        Sub Reduce(pbc As IntPtr, dwReduceHowFar As Integer, ByRef ppmkToLeft As IntPtr, ByRef ppmkReduced As IntPtr)
        Sub ComposeWith(pmkRight As IntPtr, fOnlyIfNotGeneric As Boolean, ByRef ppmkComposite As IntPtr)
        Sub [Enum](fForward As Boolean, <Out> ByRef ppenumMoniker As IEnumMoniker)
        Function IsEqual(pmkOtherMoniker As IntPtr) As Integer
        Sub Hash(<Out> ByRef pdwHash As Integer)
        Function IsRunning(pbc As IntPtr, pmkToLeft As IntPtr, pmkNewlyRunning As IntPtr) As Integer
        Sub GetTimeOfLastChange(pbc As IntPtr, pmkToLeft As IntPtr, <Out> ByRef pFileTime As Long)
        Sub Inverse(<Out> ByRef ppmk As IntPtr)
        Sub CommonPrefixWith(pmkOther As IntPtr, <Out> ByRef ppmkPrefix As IntPtr)
        Sub RelativePathTo(pmkOther As IntPtr, <Out> ByRef ppmkRelPath As IntPtr)
        Sub GetDisplayName(pbc As IntPtr, pmkToLeft As IntPtr, <Out, MarshalAs(UnmanagedType.LPWStr)> ByRef ppszDisplayName As String)
        Sub ParseDisplayName(pbc As IntPtr, pmkToLeft As IntPtr, <MarshalAs(UnmanagedType.LPWStr)> pszDisplayName As String, <Out> ByRef pchEaten As Integer, <Out> ByRef ppmkOut As IntPtr)
        Function IsSystemMoniker(<Out> ByRef pdwMksys As Integer) As Integer
    End Interface

    <ComImport()>
    <Guid("55272A00-42CB-11CE-8135-00AA004BB851")>
    <InterfaceType(ComInterfaceType.InterfaceIsIUnknown)>
    Private Interface IPropertyBag
        Function Read(<MarshalAs(UnmanagedType.LPWStr)> pszPropName As String, <MarshalAs(UnmanagedType.Struct)> ByRef pVar As Object, pErrorLog As IntPtr) As Integer
        Function Write(<MarshalAs(UnmanagedType.LPWStr)> pszPropName As String, <MarshalAs(UnmanagedType.Struct)> ByRef pVar As Object) As Integer
    End Interface

    Private Shared ReadOnly CLSID_VideoInputDeviceCategory As New Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86")
    Private Shared ReadOnly IID_IPropertyBag As New Guid("55272A00-42CB-11CE-8135-00AA004BB851")

    <DllImport("ole32.dll")>
    Private Shared Function CoInitializeEx(pvReserved As IntPtr, dwCoInit As Integer) As Integer
    End Function

    <DllImport("ole32.dll")>
    Private Shared Sub CoUninitialize()
    End Sub

    Private Const COINIT_MULTITHREADED As Integer = 0
    Private Const S_OK As Integer = 0
    Private Const S_FALSE As Integer = 1
    Private Const RPC_E_CHANGED_MODE As Integer = &H80010106

    ''' <summary>
    ''' 依 DirectShow 列舉順序（等同 OpenCvSharp DSHOW index 順序）取得所有視訊擷取裝置。
    ''' </summary>
    ''' <remarks>
    ''' .NET 執行緒集區(ThreadPool/Task.Run)背景執行緒預設「未呼叫 CoInitializeEx」，
    ''' 直接以 New CreateDevEnum() 建立 COM 物件會拋出 COMException(CO_E_NOTINITIALIZED)，
    ''' 導致列舉直接失敗、回傳空清單（相機清單顯示「未找到可用的相機設備」的常見成因之一）。
    ''' 這裡明確呼叫 CoInitializeEx，若該執行緒已在其他模式初始化過（RPC_E_CHANGED_MODE）
    ''' 則略過即可，不影響後續列舉（CLSID_SystemDeviceEnum 是 ThreadingModel=Both）。
    ''' </remarks>
    Public Shared Function GetDevices() As List(Of DsDeviceInfo)

        Dim result As New List(Of DsDeviceInfo)
        Dim comInitializedHere As Boolean = False

        Try
            Dim coHr = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED)
            If coHr = S_OK OrElse coHr = S_FALSE Then
                comInitializedHere = True
            ElseIf coHr = RPC_E_CHANGED_MODE Then
                ' 此執行緒已用不同的併發模式初始化過 COM，這是正常情況（例如 UI STA 執行緒），可繼續使用
                Logger.Debug("[DirectShowEnum] 執行緒已用不同模式初始化 COM，沿用現有初始化")
            Else
                Logger.Warn($"[DirectShowEnum] CoInitializeEx 回傳非預期結果: 0x{coHr:X8}")
            End If

            Dim devEnum As ICreateDevEnum = TryCast(New CreateDevEnum(), ICreateDevEnum)
            If devEnum Is Nothing Then Return result

            Dim enumMoniker As IEnumMoniker = Nothing
            Dim hr = devEnum.CreateClassEnumerator(CLSID_VideoInputDeviceCategory, enumMoniker, 0)

            ' hr = 1 (S_FALSE) 表示該類別下沒有裝置
            If hr <> 0 OrElse enumMoniker Is Nothing Then Return result

            Dim index As Integer = 0
            Dim fetched As IntPtr = IntPtr.Zero
            Dim monikers(0) As IMoniker

            While enumMoniker.Next(1, monikers, fetched) = 0
                Dim moniker = monikers(0)
                If moniker Is Nothing Then Exit While

                Dim name As String = ""
                Dim devicePath As String = ""

                Try
                    Dim propBagObj As Object = Nothing
                    Dim iid = IID_IPropertyBag
                    moniker.BindToStorage(IntPtr.Zero, IntPtr.Zero, iid, propBagObj)

                    Dim propBag = TryCast(propBagObj, IPropertyBag)
                    If propBag IsNot Nothing Then
                        Dim nameVal As Object = Nothing
                        propBag.Read("FriendlyName", nameVal, IntPtr.Zero)
                        name = If(nameVal?.ToString(), "")

                        Dim pathVal As Object = Nothing
                        propBag.Read("DevicePath", pathVal, IntPtr.Zero)
                        devicePath = If(pathVal?.ToString(), "")
                    End If
                Catch ex As Exception
                    Logger.Warn($"[DirectShowEnum] 讀取裝置屬性失敗 index={index}: {ex.Message}")
                End Try

                result.Add(New DsDeviceInfo With {
                    .Index = index,
                    .Name = name,
                    .DevicePath = devicePath
                })

                index += 1
            End While

        Catch ex As Exception
            Logger.Error($"[DirectShowEnum] 列舉裝置失敗: {ex.GetType().Name} - {ex.Message}")
        Finally
            If comInitializedHere Then
                Try
                    CoUninitialize()
                Catch
                    ' 忽略釋放時的例外
                End Try
            End If
        End Try

        Return result

    End Function

    ''' <summary>
    ''' 從 DevicePath 或 PNPDeviceID 中萃取可比對的標準化片段（VID/PID/序號），
    ''' 用於跨 DirectShow 與 WMI 兩種不同格式的裝置識別碼進行配對。
    ''' </summary>
    Public Shared Function NormalizeForMatch(id As String) As String
        If String.IsNullOrWhiteSpace(id) Then Return ""

        Dim s = id.ToUpperInvariant()

        ' 統一分隔符號，移除路徑/GUID等外殼裝飾，只保留 VID_xxxx、PID_xxxx、MI_xx 及序號片段
        s = s.Replace("\\?\", "").Replace("#", "\").Replace("{", "\").Replace("}", "")

        ' 移除結尾的 GLOBAL 及介面 GUID 區塊
        Dim parts = s.Split("\"c).
            Where(Function(p) Not String.IsNullOrWhiteSpace(p) AndAlso Not p.StartsWith("6994AD05") AndAlso p <> "GLOBAL").
            ToArray()

        Return String.Join("\", parts)
    End Function

    ''' <summary>
    ''' 取得裝置識別碼中「最後一段」（通常是唯一的裝置實例序號，例如 7&2D1234B6&0&0000），
    ''' 這一段在 WMI PNPDeviceID 與 DirectShow DevicePath 中格式完全一致，
    ''' 是判斷「是否為同一台實體裝置」最可靠的依據——即使兩台相機是相同型號
    ''' （VID/PID 完全相同），實例序號仍然是唯一的。
    ''' 舊有的「整串 Contains 雙向比對」在同型號多相機情境下容易誤判為同一台，
    ''' 這正是「相機仍然串線」的可能根因之一。
    ''' </summary>
    Public Shared Function ExtractInstanceId(id As String) As String
        If String.IsNullOrWhiteSpace(id) Then Return ""

        Dim s = id.ToUpperInvariant().Replace("\\?\", "").Replace("#", "\")
        Dim parts = s.Split("\"c).Where(Function(p) Not String.IsNullOrWhiteSpace(p)).ToArray()

        ' 1. 優先以包含 VID/PID 的下一段作為 InstanceId
        For i = 0 To parts.Length - 2
            If parts(i).Contains("VID_") OrElse parts(i).Contains("PID_") Then
                Return parts(i + 1)
            End If
        Next

        ' 2. 備援：過濾掉 GUID 及 GLOBAL 後取最後一段
        Dim cleanParts As New List(Of String)
        For Each p In parts
            Dim isGuid = p.Contains("-") AndAlso p.Length >= 36
            If p <> "GLOBAL" AndAlso Not isGuid Then
                cleanParts.Add(p)
            End If
        Next

        If cleanParts.Count > 0 Then
            Return cleanParts(cleanParts.Count - 1)
        End If

        Return ""
    End Function

End Class
