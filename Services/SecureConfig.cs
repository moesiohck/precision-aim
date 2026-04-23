using System;
using System.Text;

namespace AimAssistPro.Services
{
    /// <summary>
    /// Centraliza todas as strings sensíveis da aplicação.
    /// As strings são armazenadas fragmentadas como arrays de bytes XOR-encoded
    /// para que não apareçam legíveis em ferramentas de análise de binários.
    ///
    /// ─── COMO TROCAR A URL QUANDO A PRODUÇÃO ESTIVER PRONTA ────────────────
    /// 1. Abra um console app temporário C#
    /// 2. Copie o método Encode() e EncodeAsCode() para lá
    /// 3. Chame EncodeAsCode("https://minha-nova-api.com")
    /// 4. Cole o resultado em _apiBase abaixo
    /// ────────────────────────────────────────────────────────────────────────
    /// </summary>
    internal static class SecureConfig
    {
        // ─── Chave de ofuscação de strings (compilada no binário) ─────────────
        // Esta chave embaralha os bytes das strings sensíveis.
        // Impede leitura direta com strings.exe / dnSpy sem análise profunda.
        private static readonly byte[] _obfKey = {
            0x50, 0x52, 0x45, 0x43, 0x49, 0x53, 0x49, 0x4F,
            0x4E, 0x41, 0x49, 0x4D, 0x41, 0x53, 0x53, 0x49
        };

        // ─── URL da API base ──────────────────────────────────────────────────
        // Representa: "https://web-precision-alpha.vercel.app"
        private static readonly byte[] _apiBase = {
            0x38, 0x26, 0x31, 0x33, 0x3A, 0x69, 0x66, 0x60,
            0x39, 0x24, 0x2B, 0x60, 0x31, 0x21, 0x36, 0x2A,
            0x39, 0x21, 0x2C, 0x2C, 0x27, 0x7E, 0x28, 0x23,
            0x3E, 0x29, 0x28, 0x63, 0x37, 0x36, 0x21, 0x2A,
            0x35, 0x3E, 0x6B, 0x22, 0x39, 0x23
        };

        // ─── Endpoints (fragmentados para dificultar busca por strings) ─────────
        // Representa: "/api/auth/me"
        private static readonly byte[] _endpointMe = {
            0x7F, 0x33, 0x35, 0x2A, 0x66, 0x32, 0x3C, 0x3B,
            0x26, 0x6E, 0x24, 0x28
        };

        // Representa: "/api/auth/login"
        private static readonly byte[] _endpointLogin = {
            0x7F, 0x33, 0x35, 0x2A, 0x66, 0x32, 0x3C, 0x3B,
            0x26, 0x6E, 0x25, 0x22, 0x26, 0x3A, 0x3D
        };

        // Representa: "/api/auth/register"
        private static readonly byte[] _endpointRegister = {
            0x7F, 0x33, 0x35, 0x2A, 0x66, 0x32, 0x3C, 0x3B,
            0x26, 0x6E, 0x3B, 0x28, 0x26, 0x3A, 0x20, 0x3D,
            0x35, 0x20
        };

        // Representa: "/api/auth/activate"
        private static readonly byte[] _endpointActivate = {
            0x7F, 0x33, 0x35, 0x2A, 0x66, 0x32, 0x3C, 0x3B,
            0x26, 0x6E, 0x28, 0x2E, 0x35, 0x3A, 0x25, 0x28,
            0x24, 0x37
        };

        // ─── Propriedades Públicas (decodifica apenas em runtime) ─────────────

        /// <summary>URL base da API. Altere _apiBase quando a produção estiver pronta.</summary>
        public static string ApiBase => Decode(_apiBase);

        /// <summary>Endpoint: GET /api/auth/me (valida sessão salva)</summary>
        public static string EndpointMe => ApiBase + Decode(_endpointMe);

        /// <summary>Endpoint: POST /api/auth/login</summary>
        public static string EndpointLogin => ApiBase + Decode(_endpointLogin);

        /// <summary>Endpoint: POST /api/auth/register</summary>
        public static string EndpointRegister => ApiBase + Decode(_endpointRegister);

        /// <summary>Endpoint: POST /api/auth/activate</summary>
        public static string EndpointActivate => ApiBase + Decode(_endpointActivate);

        // ─── Utilitários de encode/decode ─────────────────────────────────────

        private static string Decode(byte[] data)
        {
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ _obfKey[i % _obfKey.Length]);
            return Encoding.UTF8.GetString(result);
        }

        /// <summary>
        /// Gera o byte array pronto para colar como código C#.
        /// USE APENAS EM DESENVOLVIMENTO para gerar novos arrays de strings.
        /// Exemplo: Console.WriteLine(SecureConfig.EncodeAsCode("https://minha-api.com"));
        /// </summary>
#if DEBUG
        internal static string EncodeAsCode(string plainText)
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ _obfKey[i % _obfKey.Length]);
            return "{ " + string.Join(", ", Array.ConvertAll(result, b => $"0x{b:X2}")) + " }";
        }
#endif
    }
}
