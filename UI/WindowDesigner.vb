' plugin-base/core/WindowControlHelper.vb
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media

Public Class WindowDesigner

    Private _window As Window

    Public Sub New(window As Window)
        _window = window
    End Sub

    ''' <summary>
    ''' 讓頂部拖動區可拖動窗口
    ''' </summary>
    Public Sub EnableDrag(draggableElement As UIElement)
        AddHandler draggableElement.MouseLeftButtonDown, Sub(sender, e)
                                                             _window.DragMove()
                                                         End Sub
    End Sub

    ''' <summary>
    ''' 設置按鈕的功能
    ''' </summary>
    Public Sub SetButtonActions(minBtn As Button, maxBtn As Button, closeBtn As Button)
        ' 最小化
        AddHandler minBtn.Click, Sub() _window.WindowState = WindowState.Minimized

        ' 最大化 / 還原
        AddHandler maxBtn.Click, Sub()
                                     If _window.WindowState = WindowState.Normal Then
                                         _window.WindowState = WindowState.Maximized
                                     Else
                                         _window.WindowState = WindowState.Normal
                                     End If
                                 End Sub

        ' 關閉
        AddHandler closeBtn.Click, Sub() _window.Close()

        ' Hover 效果
        Dim hoverBrush As New SolidColorBrush(Color.FromRgb(50, 50, 50))
        AddHandler minBtn.MouseEnter, Sub() minBtn.Background = hoverBrush
        AddHandler minBtn.MouseLeave, Sub() minBtn.Background = Brushes.Transparent

        AddHandler maxBtn.MouseEnter, Sub() maxBtn.Background = hoverBrush
        AddHandler maxBtn.MouseLeave, Sub() maxBtn.Background = Brushes.Transparent

        AddHandler closeBtn.MouseEnter, Sub() closeBtn.Background = Brushes.Red
        AddHandler closeBtn.MouseLeave, Sub() closeBtn.Background = Brushes.Transparent
    End Sub

End Class