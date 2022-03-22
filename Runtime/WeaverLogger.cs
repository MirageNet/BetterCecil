using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace BetterCecil
{
    public interface IWeaverLogger
    {
        void Error(string message);
        void Error(string message, MemberReference mr);
        void Error(string message, MemberReference mr, SequencePoint sequencePoint);
        void Error(string message, MethodDefinition md);

        void Warning(string message);
        void Warning(string message, MemberReference mr);
        void Warning(string message, MemberReference mr, SequencePoint sequencePoint);
        void Warning(string message, MethodDefinition md);

        List<DiagnosticMessage> GetDiagnostics();
    }

    /// <summary>
    /// Default Logger. Collects errors message for ILPostProcessResult
    /// </summary>
    public class WeaverLogger : IWeaverLogger
    {
        readonly List<DiagnosticMessage> _diagnostics = new List<DiagnosticMessage>();

        // Create Copy so private list can't be altered
        public List<DiagnosticMessage> GetDiagnostics() => _diagnostics.ToList();


        public void Error(string message)
        {
            AddMessage(message, null, DiagnosticType.Error);
        }

        public void Error(string message, MemberReference mr)
        {
            Error($"{message} (at {mr})");
        }

        public void Error(string message, MemberReference mr, SequencePoint sequencePoint)
        {
            AddMessage($"{message} (at {mr})", sequencePoint, DiagnosticType.Error);
        }

        public void Error(string message, MethodDefinition md)
        {
            Error(message, md, md.DebugInformation.SequencePoints.FirstOrDefault());
        }


        public void Warning(string message)
        {
            AddMessage($"{message}", null, DiagnosticType.Warning);
        }

        public void Warning(string message, MemberReference mr)
        {
            Warning($"{message} (at {mr})");
        }

        public void Warning(string message, MemberReference mr, SequencePoint sequencePoint)
        {
            AddMessage($"{message} (at {mr})", sequencePoint, DiagnosticType.Warning);
        }

        public void Warning(string message, MethodDefinition md)
        {
            Warning(message, md, md.DebugInformation.SequencePoints.FirstOrDefault());
        }


        private void AddMessage(string message, SequencePoint sequencePoint, DiagnosticType diagnosticType)
        {
            _diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = diagnosticType,
                File = sequencePoint?.Document.Url.Replace($"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}", ""),
                Line = sequencePoint?.StartLine ?? 0,
                Column = sequencePoint?.StartColumn ?? 0,
                MessageData = message
            });
        }
    }
}
