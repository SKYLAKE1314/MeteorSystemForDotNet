Imports System.Runtime.InteropServices
Imports System.Windows.Interop
Public Class Startup
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)

        ApplyAcrylic(Me)
    End Sub

    Private Sub ApplyAcrylic(win As Window)

        Dim hwnd = New WindowInteropHelper(win).Handle

        Dim accent As New AccentPolicy()
        accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND

        Dim size As Integer = Marshal.SizeOf(accent)
        Dim ptr = Marshal.AllocHGlobal(size)
        Marshal.StructureToPtr(accent, ptr, False)

        Dim data As New WindowCompositionAttributeData()
        data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY
        data.SizeOfData = size
        data.Data = ptr

        SetWindowCompositionAttribute(hwnd, data)

        Marshal.FreeHGlobal(ptr)

    End Sub

#Region "Win32"

    <StructLayout(LayoutKind.Sequential)>
    Private Structure AccentPolicy
        Public AccentState As AccentState
        Public AccentFlags As Integer
        Public GradientColor As Integer
        Public AnimationId As Integer
    End Structure

    Private Enum AccentState
        ACCENT_DISABLED = 0
        ACCENT_ENABLE_BLURBEHIND = 3
    End Enum

    <StructLayout(LayoutKind.Sequential)>
    Private Structure WindowCompositionAttributeData
        Public Attribute As WindowCompositionAttribute
        Public Data As IntPtr
        Public SizeOfData As Integer
    End Structure

    Private Enum WindowCompositionAttribute
        WCA_ACCENT_POLICY = 19
    End Enum

    <DllImport("user32.dll")>
    Private Shared Function SetWindowCompositionAttribute(hwnd As IntPtr,
        ByRef data As WindowCompositionAttributeData) As Integer
    End Function

#End Region

    Public Sub UpdateProgress(p As Integer, msg As String)

        Dispatcher.Invoke(Sub()

                              StatusText.Text = msg
                              PercentText.Text = $"{p}%"

                              If p > 0 Then
                                  ProgressBar1.IsIndeterminate = False
                                  ProgressBar1.Value = p
                              End If

                          End Sub)

    End Sub

End Class