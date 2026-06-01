Imports System
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows.Media.Media3D
Imports MetroSystemForDotNet
Imports HalconDotNet
Imports MVSDK_Net

'Namespace 

Public Class IraypleCamera
        Implements ICamera, IDisposable

        Public ReadOnly Property Name As String Implements ICamera.Name
            Get
                Return "大华相机"
            End Get
        End Property

        Public ReadOnly Property Version As String Implements ICamera.Version
            Get
                Return "1.0.0"
            End Get
        End Property

        Public ReadOnly Property Description As String Implements ICamera.Description
            Get
                Return "大华相机插件"
            End Get
        End Property

        ' ===== Fields =====
        Private cam As New MyCamera()
        Private m_camNames As New List(Of String)()
        Private _receiveThread As Thread
        Private _isGrabbing As Boolean
        Private _frameMutex As New Mutex()
        Private _currentFrame As IMVDefine.IMV_Frame
        Private _driverBuffer As IntPtr = IntPtr.Zero
        Private _driverBufferSize As UInteger = 0
        Private _frameInfo As IMVDefine.IMV_FrameInfo
        Private m_cameraName As String
        Private m_nDataLength As Integer = 0
        Private m_pDstData As IntPtr

    ' ===== Event =====
    Public Event OnFrameGrabbed As Action(Of CameraFrame) Implements ICamera.OnFrameGrabbed

    ' ===== DllImports =====
    <DllImport("kernel32.dll", EntryPoint:="CopyMemory", SetLastError:=False)>
        Private Shared Sub CopyMemory(dest As IntPtr, src As IntPtr, count As Integer)
        End Sub

        ' ===== Methods =====

        Public Function EnumCamera(ByRef camNames As List(Of String)) As Boolean Implements ICamera.EnumCamera
            camNames = New List(Of String)()
            Dim devList As New IMVDefine.IMV_DeviceList()
            Dim res As Integer = MyCamera.IMV_EnumDevices(devList, CType(IMVDefine.IMV_EInterfaceType.interfaceTypeAll, UInteger))

            If res <> IMVDefine.IMV_OK OrElse devList.nDevNum = 0 Then Return False

            Dim structSize As Integer = Marshal.SizeOf(Of IMVDefine.IMV_DeviceInfo)()
            For i As Integer = 0 To devList.nDevNum - 1
                Dim info As IMVDefine.IMV_DeviceInfo = Marshal.PtrToStructure(Of IMVDefine.IMV_DeviceInfo)(
                    IntPtr.Add(devList.pDevInfo, i * structSize))
                camNames.Add(info.cameraName)
            Next

            m_camNames = New List(Of String)(camNames)
            Return True
        End Function

        Public Function Open(camName As String, ByRef pixelTypes As List(Of String)) As String Implements ICamera.Open
            m_camNames.Clear()
            EnumCamera(m_camNames)

            pixelTypes = New List(Of String)()
            Dim idx As Integer = m_camNames.IndexOf(camName)
            If idx < 0 Then Return $"Camera [{camName}] not found"

            m_cameraName = camName

            ' 創建句柄
            Dim res As Integer = cam.IMV_CreateHandle(IMVDefine.IMV_ECreateHandleMode.modeByIndex, idx)
            If res <> IMVDefine.IMV_OK Then Return $"CreateHandle failed: {res}"

            ' 打開設備
            res = cam.IMV_Open()
            If res <> IMVDefine.IMV_OK Then Return $"Open camera failed: {res}"

        ' TODO: 枚舉像素格式 + 設置內部 Buffer、觸發模式等（復用 C# 邏輯）

        Return String.Empty
        End Function

        Public Function Start(camName As String) As String Implements ICamera.Start
            _isGrabbing = True
            _receiveThread = New Thread(AddressOf ReceiveThreadProcess) With {.IsBackground = True}
            _receiveThread.Start()

            Dim res As Integer = cam.IMV_StartGrabbing()
            If res <> IMVDefine.IMV_OK Then
                _isGrabbing = False
                _receiveThread.Join()
                Return $"StartGrabbing failed: {res}"
            End If
            Return String.Empty
        End Function

    Public Function [Stop](camName As String) As String Implements ICamera.Stop
        _isGrabbing = False
        Dim res As Integer = cam.IMV_StopGrabbing()
        _receiveThread?.Join()
        RaiseEvent OnFrameGrabbed(Nothing) ' 可選，解除事件
        Return If(res = IMVDefine.IMV_OK, String.Empty, $"StopGrabbing failed: {res}")
    End Function
    Public Function Close(camName As String) As String Implements ICamera.Close
        Try
            If cam.IMV_IsGrabbing() Then
                [Stop](camName)
            End If
            cam.IMV_Close()
            Return String.Empty
        Catch ex As Exception
            Return $"Close error: {ex.Message}"
        End Try
    End Function


    Private Sub ReceiveThreadProcess()
            While _isGrabbing
                If cam.IMV_GetFrame(_currentFrame, 1000) = IMVDefine.IMV_OK Then
                    ' 這裡復用 ConvertToBGR24 + Halcon 轉換邏輯
                End If
            End While
        End Sub

        Public Function SoftwareTrigger(camName As String) As String Implements ICamera.SoftwareTrigger
            ' TODO: 直接復用 C# 邏輯
            Return String.Empty
        End Function

        Public Function SetExposureTime(camName As String, value As Single) As String Implements ICamera.SetExposureTime
            cam.IMV_SetDoubleFeatureValue("ExposureTime", value)
            Return String.Empty
        End Function

        ' ===== IDisposable =====
        Public Sub Dispose() Implements IDisposable.Dispose
            _isGrabbing = False
            _receiveThread?.Join()
            If _driverBuffer <> IntPtr.Zero Then Marshal.FreeHGlobal(_driverBuffer)
            If m_pDstData <> IntPtr.Zero Then Marshal.FreeHGlobal(m_pDstData)
            cam?.IMV_Close()
        End Sub

    End Class

'End Namespace
