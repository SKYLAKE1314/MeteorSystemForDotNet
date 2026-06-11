Imports System
Imports System.Windows.Controls
Imports System.Windows.Documents
Imports System.Windows.Media
Imports System.Diagnostics
Imports System.IO

Public Class Logger

    ' ===== WPF RichTextBox =====
    Private Shared _wpfRtb As RichTextBox
    Private Shared MaxLogLines As Integer = 1000 ' 最多行數限制
    Public Shared Event LogReceived(level As String, message As String)
    Public Shared Sub SetWpfRichTextBox(rtb As RichTextBox)
        _wpfRtb = rtb
    End Sub

    ' ===== 日志方法 =====
    Public Shared Sub Debug(msg As String)
        Dim logStr = AppendClassLine(msg)
        WriteToWpfUI("DEBUG", logStr)
        ' 這裡可加入文件/系統日志
    End Sub

    Public Shared Sub Info(msg As String)
        Dim logStr = AppendClassLine(msg)
        WriteToWpfUI("INFO", logStr)
    End Sub

    Public Shared Sub Warn(msg As String)
        Dim logStr = AppendClassLine(msg)
        WriteToWpfUI("WARN", logStr)
    End Sub

    Public Shared Sub [Error](msg As String)
        Dim logStr = AppendClassLine(msg)
        WriteToWpfUI("ERROR", logStr)
    End Sub

    ' ===== 核心寫入 UI =====
    Private Shared Sub WriteToWpfUI(level As String, message As String)
        If _wpfRtb Is Nothing Then Return

        _wpfRtb.Dispatcher.Invoke(Sub()
                                      ' 超過行數就清理
                                      If _wpfRtb.Document.Blocks.Count > MaxLogLines Then
                                          _wpfRtb.Document.Blocks.Clear()
                                      End If

                                      Dim para As New Paragraph()
                                      Dim run As New Run($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}")

                                      Select Case level
                                          Case "DEBUG"
                                              run.Foreground = Brushes.Blue
                                          Case "INFO"
                                              run.Foreground = Brushes.Black
                                          Case "WARN"
                                              run.Foreground = Brushes.Orange
                                          Case "ERROR"
                                              run.Foreground = Brushes.Red
                                      End Select

                                      para.Inlines.Add(run)
                                      _wpfRtb.Document.Blocks.Add(para)
                                      _wpfRtb.ScrollToEnd()
                                  End Sub)
        RaiseEvent LogReceived(level, message)
    End Sub

    ' ===== 附加調試信息 =====
    Private Shared Function AppendClassLine(msg As String) As String
        Try
            Dim st As New StackTrace(True)
            Dim sf As StackFrame = st.GetFrame(2)
            Return $"{msg} [{Path.GetFileName(sf.GetFileName())}:{sf.GetFileLineNumber()}]"
        Catch
            Return msg
        End Try
    End Function

End Class