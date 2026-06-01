Imports System
Imports System.Runtime.InteropServices

'Namespace

Public Class Render
        Implements IDisposable

        Private ReadOnly _sdkFilePath As String
        Private _isOpen As Boolean
        Private _hWnd As IntPtr
        Private _handler As IntPtr
        Private _params As VR_OPEN_PARAM_S

        ''' <summary>
        ''' 初始化 Render，傳入 WPF Window/Frame Handle
        ''' </summary>
        ''' <param name="hWnd">窗口句柄</param>
        ''' <param name="dllPath">VideoRender.dll 路徑，可選，預設當前目錄</param>
        Public Sub New(hWnd As IntPtr, Optional dllPath As String = ".\VideoRender.dll")
            _hWnd = hWnd
            _handler = IntPtr.Zero
            _sdkFilePath = dllPath
        End Sub

        ' ===== DLL Import =====
        <DllImport(".\VideoRender.dll", CallingConvention:=CallingConvention.StdCall)>
        Private Shared Function VR_Open(ByRef pParam As VR_OPEN_PARAM_S, ByRef pHandle As IntPtr) As VR_ERR_E
        End Function

        <DllImport(".\VideoRender.dll", CallingConvention:=CallingConvention.StdCall)>
        Private Shared Function VR_RenderFrame(handle As IntPtr, ByRef param As VR_FRAME_S, ByRef pEnlargeRect As VR_Rect) As VR_ERR_E
        End Function

        <DllImport(".\VideoRender.dll", CallingConvention:=CallingConvention.StdCall)>
        Private Shared Function VR_Close(handle As IntPtr) As VR_ERR_E
        End Function

        ' ===== Structs & Enums =====
        <StructLayout(LayoutKind.Sequential)>
        Public Structure VR_FRAME_S
            <MarshalAs(UnmanagedType.ByValArray, SizeConst:=4, ArraySubType:=UnmanagedType.SysUInt)>
            Public data() As IntPtr

            <MarshalAs(UnmanagedType.ByValArray, SizeConst:=4, ArraySubType:=UnmanagedType.I4)>
            Public steide() As Integer

            Public nWidth As Integer
            Public nHeight As Integer
            Public format As VR_PIXEL_TYPE_E
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Public Structure VR_Rect
            Public left As Integer
            Public top As Integer
            Public right As Integer
            Public bottom As Integer
        End Structure

        Public Enum VR_ERR_E
            VR_Success
            VR_ILLEGAL_PARAM
            VR_ERR_ORDER
            VR_NO_MEMORY
            VR_NOT_SUPPORT
            VR_D3D_PRESENT_FAILED
            VR_GDI_CREATE_OBJ_FAILED
            VR_DEFAULT_FONT_NOT_EXIST
        End Enum

        Public Enum VR_MODE_E
            VR_MODE_D3D = 0
            VR_MODE_GDI
            VR_MODE_OPENGLX
            VR_MODE_X11
        End Enum

        Public Enum VR_PIXEL_TYPE_E
            VR_PIXEL_FMT_NONE = -1
            VR_PIXEL_FMT_YUV420P
            VR_PIXEL_FMT_RGB24
            VR_PIXEL_FMT_MONO8
        End Enum

        <StructLayout(LayoutKind.Sequential)>
        Public Structure VR_OPEN_PARAM_S
            Public hWnd As IntPtr
            Public eVideoRenderMode As VR_MODE_E
            Public nWidth As Integer
            Public nHeight As Integer
        End Structure

        ' ===== 功能方法 =====
        Public Function Open(Optional width As Integer = 16, Optional height As Integer = 16) As Boolean
            If _hWnd = IntPtr.Zero Then Return False

            _params.eVideoRenderMode = VR_MODE_E.VR_MODE_GDI
            _params.hWnd = _hWnd
            _params.nWidth = width
            _params.nHeight = height

            Dim ret As VR_ERR_E = VR_Open(_params, _handler)
            If ret <> VR_ERR_E.VR_Success Then Return False

            _isOpen = True
            Return True
        End Function

        Public Function Display(displayBuffer As IntPtr, iWidth As Integer, iHeight As Integer, iPixelFormat As VR_PIXEL_TYPE_E) As Boolean
            If displayBuffer = IntPtr.Zero OrElse iWidth = 0 OrElse iHeight = 0 Then Return False

            ' 檢查尺寸是否改變
            If _isOpen AndAlso (_params.nWidth <> iWidth OrElse _params.nHeight <> iHeight) Then
                Close()
            End If

            If Not _isOpen Then
                If Not Open(iWidth, iHeight) Then Return False
            End If

            If _isOpen Then
                Dim renderParam As New VR_FRAME_S()
                renderParam.data = New IntPtr(3) {}
                renderParam.steide = New Integer(3) {}
                renderParam.data(0) = displayBuffer
                renderParam.steide(0) = iWidth
                renderParam.nWidth = iWidth
                renderParam.nHeight = iHeight
                renderParam.format = If(iPixelFormat = VR_PIXEL_TYPE_E.VR_PIXEL_FMT_MONO8, VR_PIXEL_TYPE_E.VR_PIXEL_FMT_MONO8, VR_PIXEL_TYPE_E.VR_PIXEL_FMT_RGB24)

                Dim rect As New VR_Rect With {
                    .left = 0,
                    .top = 0,
                    .right = iWidth,
                    .bottom = iHeight
                }

                Dim ret As VR_ERR_E = VR_RenderFrame(_handler, renderParam, rect)
                Return ret = VR_ERR_E.VR_Success
            End If

            Return False
        End Function

        Public Function Close() As Boolean
            If _handler <> IntPtr.Zero Then
                VR_Close(_handler)
                _handler = IntPtr.Zero
                _isOpen = False
            End If
            Return True
        End Function

        ' ===== IDisposable =====
        Private _disposed As Boolean = False

        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not _disposed Then
                If disposing Then
                    ' 釋放托管資源
                End If

                ' 釋放非托管資源
                Close()
            End If
            _disposed = True
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(True)
            GC.SuppressFinalize(Me)
        End Sub

        Protected Overrides Sub Finalize()
            Dispose(False)
        End Sub

    End Class

'End Namespace
