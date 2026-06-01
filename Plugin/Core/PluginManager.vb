Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Reflection
Imports MetroSystemForDotNet

Public Class PluginManager

    ''' <summary>
    ''' 加载单个插件，返回第一个能实例化的 T
    ''' </summary>
    Public Shared Function LoadPlugin(Of T As {Class})(dllPath As String) As T
        Dim assembly As Assembly = Nothing
        Try
            assembly = Assembly.LoadFrom(dllPath)
        Catch ex As Exception
            Logger.Warn($"加载程序集失败：{dllPath}{vbCrLf}{ex.Message}")
            Return Nothing
        End Try

        Dim types() As Type = Nothing
        Try
            types = assembly.GetTypes()
        Catch rtlEx As ReflectionTypeLoadException
            For Each loaderEx In rtlEx.LoaderExceptions
                Logger.Warn($"反射加载类型时异常: {loaderEx.Message}")
            Next
            types = rtlEx.Types.Where(Function(tp) tp IsNot Nothing).ToArray()
        End Try

        Dim pluginType = types.
        Where(Function(tp) GetType(T).IsAssignableFrom(tp) AndAlso tp.IsClass AndAlso Not tp.IsAbstract).
        FirstOrDefault()

        If pluginType Is Nothing Then
            Logger.Warn($"在 {dllPath} 中未找到接口 {GetType(T).Name} 的实现。")
            Return Nothing
        End If

        Try
            Dim plugin As T = TryCast(Activator.CreateInstance(pluginType), T)
            If plugin IsNot Nothing Then
                Logger.Info($"插件实例化成功：{pluginType.FullName}")
                Return plugin
            Else
                Logger.Warn($"Activator.CreateInstance 返回 null：{pluginType.FullName}")
            End If
        Catch ex As Exception
            Logger.Error($"实例化插件类型失败: {pluginType.FullName}{vbCrLf}{ex}")
        End Try

        Return Nothing
    End Function

End Class