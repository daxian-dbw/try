﻿using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace MLS.Agent.Markdown
{
    public class CodeLinkBlockParser : FencedBlockParserBase<CodeLinkBlock>
    {
        private readonly Parser _csharpLinkParser;

        private readonly IDirectoryAccessor _directoryAccessor;

        public CodeLinkBlockParser(IDirectoryAccessor directoryAccessor)
        {
            OpeningCharacters = new[] { '`' };
            InfoParser = ParseCodeOptions;
            _directoryAccessor = directoryAccessor ?? throw new ArgumentNullException(nameof(directoryAccessor));
            _csharpLinkParser = CodeLinkBlock.CreateOptionsParser(_directoryAccessor);
        }

        protected override CodeLinkBlock CreateFencedBlock(BlockProcessor processor) =>
            new CodeLinkBlock(this, _directoryAccessor);

        private bool ParseCodeOptions(
            BlockProcessor state,
            ref StringSlice line,
            IFencedBlock fenced)
        {
            var codeLinkBlock = fenced as CodeLinkBlock;

            if (codeLinkBlock == null)
            {
                return false;
            }

            var parseResult = _csharpLinkParser.Parse(line.ToString());

            if (parseResult.CommandResult.Name != "csharp")
            {
                return false;
            }

            codeLinkBlock.Region = parseResult.ValueForOption<string>("region");

            codeLinkBlock.Session = parseResult.ValueForOption<string>("session");

            if (parseResult.CommandResult.ArgumentResult is SuccessfulArgumentResult)
            {
                codeLinkBlock.SourceFile = parseResult.CommandResult.GetValueOrDefault<RelativeFilePath>();
            }

            if (codeLinkBlock.SourceFile != null)
            {
                codeLinkBlock.Lines = new StringLineGroup(codeLinkBlock.SourceCode);
            }

            if (parseResult.Errors.Any())
            {
                var errors = parseResult.Errors.Select(e => e.ToString());

                foreach (var error in errors)
                {
                    codeLinkBlock.AddDiagnostic(error);
                }
            }

            var projectOptionResult = parseResult.CommandResult["project"];
            var packageOptionResult = parseResult.CommandResult["package"];


            if (packageOptionResult?.ArgumentResult is SuccessfulArgumentResult)
            {
                codeLinkBlock.Package = parseResult.CommandResult.ValueForOption<string>("package");
            }

            if (codeLinkBlock.Package == null && projectOptionResult?.ArgumentResult is SuccessfulArgumentResult)
            {
                codeLinkBlock.ProjectFile = parseResult.CommandResult.ValueForOption<FileInfo>("project");

                var packageName = GetPackageNameFromProjectFile(codeLinkBlock.ProjectFile);

                if (packageName == null &&
                    codeLinkBlock.SourceFile != null)
                {
                    codeLinkBlock.AddDiagnostic(
                        $"No project file could be found at path {_directoryAccessor.GetFullyQualifiedPath(new RelativeDirectoryPath("."))}");
                }
            }

            if (codeLinkBlock.Package != null && !projectOptionResult.IsImplicit)
            {
                codeLinkBlock.AddDiagnostic("Can't specify both --project and --package");
            }

            return true;
        }

        private static string GetPackageNameFromProjectFile(FileInfo projectFile)
        {
            return projectFile?.FullName;
        }

        public override BlockState TryContinue(BlockProcessor processor, Block block)
        {
            var fence = (IFencedBlock)block;
            var count = fence.FencedCharCount;
            var matchChar = fence.FencedChar;
            var c = processor.CurrentChar;

            // Match if we have a closing fence
            var line = processor.Line;
            while (c == matchChar)
            {
                c = line.NextChar();
                count--;
            }

            // If we have a closing fence, close it and discard the current line
            // The line must contain only fence opening character followed only by whitespaces.
            if (count <= 0 && !processor.IsCodeIndent && (c == '\0' || c.IsWhitespace()) && line.TrimEnd())
            {
                block.UpdateSpanEnd(line.Start - 1);

                // Don't keep the last line
                return BlockState.BreakDiscard;
            }

            // Reset the indentation to the column before the indent
            processor.GoToColumn(processor.ColumnBeforeIndent);

            var codeBlock = block as CodeLinkBlock;

            //if we already have the source code discard the lines that are inside the fenced code
            if (codeBlock != null && !string.IsNullOrWhiteSpace(codeBlock.SourceCode))
            {
                return BlockState.ContinueDiscard;
            }

            return BlockState.Continue;
        }
    }
}