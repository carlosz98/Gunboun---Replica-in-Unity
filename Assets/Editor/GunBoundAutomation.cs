using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// File-driven editor automation used for testing (safe to delete).
/// Drop an empty file into &lt;project&gt;/Automation/ to trigger an action:
///   setup.request  → runs GunBound ▸ Setup Scene
///   play.request   → enters Play mode
///   stop.request   → exits Play mode
/// Console output is mirrored to Automation/log.txt and compiler messages to Automation/compile.txt.
/// </summary>
[InitializeOnLoad]
public static class GunBoundAutomation
{
    static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Automation"));
    static double nextPoll;

    static GunBoundAutomation()
    {
        Directory.CreateDirectory(Dir);
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        Application.logMessageReceivedThreaded -= OnLog;
        Application.logMessageReceivedThreaded += OnLog;
        CompilationPipeline.assemblyCompilationFinished -= OnCompiled;
        CompilationPipeline.assemblyCompilationFinished += OnCompiled;
        Append("log.txt", $"[{Stamp()}] editor scripts loaded (playing={EditorApplication.isPlayingOrWillChangePlaymode})");
    }

    static string Stamp() => DateTime.Now.ToString("HH:mm:ss");

    static void Append(string file, string line)
    {
        try { File.AppendAllText(Path.Combine(Dir, file), line + Environment.NewLine); }
        catch { /* best effort */ }
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        string trace = type == LogType.Log ? "" : "\n    " + (stackTrace ?? "").Replace("\n", "\n    ").TrimEnd();
        Append("log.txt", $"[{Stamp()}] {type}: {condition}{trace}");
    }

    static void OnCompiled(string assembly, CompilerMessage[] messages)
    {
        foreach (var m in messages)
            Append("compile.txt", $"[{Stamp()}] {m.type} {Path.GetFileName(assembly)}: {m.file}({m.line}): {m.message}");
        Append("compile.txt", $"[{Stamp()}] compiled {Path.GetFileName(assembly)} ({messages.Length} message(s))");
    }

    static bool Take(string name)
    {
        string p = Path.Combine(Dir, name);
        if (!File.Exists(p)) return false;
        try { File.Delete(p); } catch { return false; }
        return true;
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 0.5;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        if (Take("refresh.request")) AssetDatabase.Refresh();

        if (!EditorApplication.isPlayingOrWillChangePlaymode && Take("setup.request"))
        {
            try
            {
                GunBoundSetup.Setup();
                Append("log.txt", $"[{Stamp()}] SETUP DONE");
            }
            catch (Exception e)
            {
                Append("log.txt", $"[{Stamp()}] SETUP FAILED: {e}");
            }
        }

        if (!EditorApplication.isPlayingOrWillChangePlaymode && Take("play.request"))
        {
            Append("log.txt", $"[{Stamp()}] entering play mode");
            EditorApplication.isPlaying = true;
        }

        if (EditorApplication.isPlaying && Take("stop.request"))
        {
            Append("log.txt", $"[{Stamp()}] exiting play mode");
            EditorApplication.isPlaying = false;
        }
    }
}
