using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Leitura de entrada de zip compartilhada entre os formatos de pacote
    /// (<c>.uiexport</c>/<c>.uikit</c> em <see cref="UIExportPackage"/> e <c>.uikitset</c> em
    /// <see cref="UIKitSetPackage"/>).
    /// </summary>
    /// <remarks>
    /// O que está aqui é justamente a parte que não pode divergir entre formatos: contar bytes
    /// de verdade em vez de confiar no tamanho declarado pelo zip, decodificar UTF-8 de forma
    /// estrita, e checar a assinatura de PNG byte a byte. Duplicar isso por formato de pacote
    /// deixaria um fix de segurança feito num lado e esquecido no outro.
    /// </remarks>
    internal static class ZipEntryReader
    {
        private static readonly byte[] PngSignature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// Lê uma entrada contando os bytes de verdade.
        /// </summary>
        /// <remarks>
        /// <see cref="ZipArchiveEntry.Length"/> é o tamanho <i>declarado</i> no cabeçalho
        /// do zip e pode mentir — é exatamente o que uma bomba de descompressão faz. A
        /// única checagem que vale é contar o que realmente sai do stream, e parar no
        /// limite.
        /// </remarks>
        /// <param name="maxTotalBytesForMessage">
        /// Só para a mensagem de erro do estouro do orçamento total: o valor inicial de
        /// <paramref name="totalBudget"/> já foi descontado quando o estouro é detectado, então
        /// não dá para recuperar "qual era o limite" a partir dele.
        /// </param>
        internal static byte[] ReadCounted(
            ZipArchiveEntry entry,
            long maxEntryBytes,
            ref long totalBudget,
            string packageName,
            long maxTotalBytesForMessage)
        {
            using Stream source = entry.Open();
            using var buffer = new MemoryStream();

            byte[] chunk = new byte[81920];
            long read = 0;

            while (true)
            {
                int count = source.Read(chunk, 0, chunk.Length);
                if (count <= 0)
                {
                    break;
                }

                read += count;
                totalBudget -= count;

                if (read > maxEntryBytes)
                {
                    throw new UIExportException(
                        $"'{entry.FullName}' em {packageName} passa de " +
                        $"{Megabytes(maxEntryBytes)} MB descomprimido.");
                }

                if (totalBudget < 0)
                {
                    throw new UIExportException(
                        $"{packageName} passa de {Megabytes(maxTotalBytesForMessage)} MB " +
                        "descomprimido no total.");
                }

                buffer.Write(chunk, 0, count);
            }

            return buffer.ToArray();
        }

        /// <summary>Decodifica UTF-8 estrito, rejeitando bytes inválidos.</summary>
        /// <remarks>
        /// <paramref name="entryLabel"/> só entra na mensagem de erro — um texto que não é
        /// UTF-8 válido não deve virar caracteres de substituição que confundem o diagnóstico,
        /// e dizer qual entrada falhou poupa o dev de abrir o zip para descobrir.
        /// </remarks>
        internal static string DecodeUtf8(byte[] bytes, string entryLabel)
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

            try
            {
                int offset = HasUtf8Bom(bytes) ? 3 : 0;
                return utf8.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException error)
            {
                throw new UIExportException($"{entryLabel} não está em UTF-8 válido.", error);
            }
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        internal static bool HasPngSignature(byte[] bytes)
        {
            if (bytes.Length < PngSignature.Length)
            {
                return false;
            }

            for (int i = 0; i < PngSignature.Length; i++)
            {
                if (bytes[i] != PngSignature[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Sanitiza o nome antes de colocá-lo numa mensagem de erro: o nome vem do zip e
        /// não deve conseguir injetar controle nem poluir o log com algo gigante.
        /// </summary>
        internal static string Describe(string entryName)
        {
            var builder = new StringBuilder(64);
            int limit = Math.Min(entryName.Length, 60);

            for (int i = 0; i < limit; i++)
            {
                char c = entryName[i];
                builder.Append(char.IsControl(c) ? '?' : c);
            }

            if (entryName.Length > limit)
            {
                builder.Append("...");
            }

            return builder.ToString();
        }

        internal static long Megabytes(long bytes)
        {
            return bytes / (1024 * 1024);
        }
    }
}
