Imports HalconDotNet
Imports System
Imports System.Collections.Generic

' ===== 插件層面可識別的像素格式 =====
Public Enum CameraPixelFormat
    Mono8
    RGB24
End Enum

' ===== 插件對外暴露的幀信息 =====
Public Class CameraFrame
    ''' <summary>原始圖像字節數組</summary>
    Public Property Image As HImage

    ''' <summary>幀抓取時間戳</summary>
    Public Property TimeStamp As DateTime

    ''' <summary>相機名稱</summary>
    Public Property CameraName As String
End Class

' ===== 相機接口 =====
Public Interface ICamera
    Inherits IDisposable

    ''' <summary>
    ''' 當有一幀數據抓取到時觸發
    ''' </summary>
    Event OnFrameGrabbed As Action(Of CameraFrame)

    ''' <summary>枚舉相機</summary>
    Function EnumCamera(ByRef camNames As List(Of String)) As Boolean

    Function Open(camName As String, ByRef pixelTypes As List(Of String)) As String
    Function SoftwareTrigger(camName As String) As String
    Function Start(camName As String) As String
    Function [Stop](camName As String) As String
    Function Close(camName As String) As String
    Function SetExposureTime(camName As String, value As Single) As String

    ''' <summary>插件名稱（用於 UI 顯示或配置）</summary>
    ReadOnly Property Name As String

    ''' <summary>插件版本</summary>
    ReadOnly Property Version As String

    ''' <summary>插件描述（幫助調試、日誌記錄）</summary>
    ReadOnly Property Description As String

End Interface
