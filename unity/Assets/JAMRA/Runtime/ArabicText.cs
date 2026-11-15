// ════════════════════════════════════════════════════════════
//  جَمرة — تشكيل النصّ العربي لواجهة IMGUI
//  IMGUI في يونتي لا يصل الحروف ولا يعكس الاتّجاه، فيظهر العربي
//  مقطّعاً ومقلوباً. هنا نختار شكل كل حرف (منفرد/أوّل/وسط/آخر)
//  من «أشكال العرض العربية» ثم نعكس السطر مع إبقاء الأرقام
//  واللاتيني على اتّجاهها.
// ════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;

namespace Jamra
{
    public static class ArabicText
    {
        // لكل حرف: رمز الشكل المنفرد، وعدد الأشكال (2 = لا يتّصل بما بعده، 4 = يتّصل)
        static readonly Dictionary<char, int[]> Forms = new Dictionary<char, int[]>
        {
            { 'ء', new[] { 0xFE80, 1 } }, { 'آ', new[] { 0xFE81, 2 } },
            { 'أ', new[] { 0xFE83, 2 } }, { 'ؤ', new[] { 0xFE85, 2 } },
            { 'إ', new[] { 0xFE87, 2 } }, { 'ئ', new[] { 0xFE89, 4 } },
            { 'ا', new[] { 0xFE8D, 2 } }, { 'ب', new[] { 0xFE8F, 4 } },
            { 'ة', new[] { 0xFE93, 2 } }, { 'ت', new[] { 0xFE95, 4 } },
            { 'ث', new[] { 0xFE99, 4 } }, { 'ج', new[] { 0xFE9D, 4 } },
            { 'ح', new[] { 0xFEA1, 4 } }, { 'خ', new[] { 0xFEA5, 4 } },
            { 'د', new[] { 0xFEA9, 2 } }, { 'ذ', new[] { 0xFEAB, 2 } },
            { 'ر', new[] { 0xFEAD, 2 } }, { 'ز', new[] { 0xFEAF, 2 } },
            { 'س', new[] { 0xFEB1, 4 } }, { 'ش', new[] { 0xFEB5, 4 } },
            { 'ص', new[] { 0xFEB9, 4 } }, { 'ض', new[] { 0xFEBD, 4 } },
            { 'ط', new[] { 0xFEC1, 4 } }, { 'ظ', new[] { 0xFEC5, 4 } },
            { 'ع', new[] { 0xFEC9, 4 } }, { 'غ', new[] { 0xFECD, 4 } },
            { 'ف', new[] { 0xFED1, 4 } }, { 'ق', new[] { 0xFED5, 4 } },
            { 'ك', new[] { 0xFED9, 4 } }, { 'ل', new[] { 0xFEDD, 4 } },
            { 'م', new[] { 0xFEE1, 4 } }, { 'ن', new[] { 0xFEE5, 4 } },
            { 'ه', new[] { 0xFEE9, 4 } }, { 'و', new[] { 0xFEED, 2 } },
            { 'ى', new[] { 0xFEEF, 2 } }, { 'ي', new[] { 0xFEF1, 4 } },
        };

        static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        public static string Fix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string r;
            if (Cache.TryGetValue(s, out r)) return r;
            if (Cache.Count > 600) Cache.Clear();
            r = Reorder(Shape(Strip(s)));
            Cache[s] = r;
            return r;
        }

        // الحركات تُحذف: بعد عكس السطر تسبق حرفها فتظهر في غير موضعها
        static string Strip(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (!((c >= 'ً' && c <= 'ْ') || c == 'ٰ')) sb.Append(c);
            return sb.ToString();
        }

        static bool JoinsNext(char c)
        {
            if (c == 'ـ') return true;
            int[] f;
            return Forms.TryGetValue(c, out f) && f[1] == 4;
        }

        static bool JoinsPrev(char c)
        {
            if (c == 'ـ') return true;
            int[] f;
            return Forms.TryGetValue(c, out f) && f[1] >= 2;
        }

        static string Shape(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool prevJoins = false;         // هل الحرف السابق يتّصل بهذا؟
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int[] f;
                if (!Forms.TryGetValue(c, out f))
                {
                    sb.Append(c);
                    prevJoins = c == 'ـ';
                    continue;
                }
                char next = i + 1 < s.Length ? s[i + 1] : '\0';

                // لام + ألف = حرف مركّب واحد
                if (c == 'ل')
                {
                    int lig = next == 'آ' ? 0xFEF5 : next == 'أ' ? 0xFEF7
                            : next == 'إ' ? 0xFEF9 : next == 'ا' ? 0xFEFB : 0;
                    if (lig != 0)
                    {
                        sb.Append((char)(lig + (prevJoins ? 1 : 0)));
                        prevJoins = false;
                        i++;
                        continue;
                    }
                }

                bool nextJoins = f[1] == 4 && JoinsPrev(next);
                int form = prevJoins && nextJoins ? 3 : prevJoins && f[1] >= 2 ? 1 : nextJoins ? 2 : 0;
                sb.Append((char)(f[0] + form));
                prevJoins = nextJoins;
            }
            return sb.ToString();
        }

        static bool IsLtr(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        static bool IsLtrInner(char c)
        {
            return c == '.' || c == ',' || c == '/' || c == ':' || c == '-' || c == '+' || c == '%';
        }

        // يعكس السطر كلّه، ثم يعيد كل مقطع لاتيني/رقمي إلى اتّجاهه
        static string Reorder(string s)
        {
            bool anyArabic = false;
            foreach (char c in s) if (c >= 'ﹰ' && c <= '﻿') { anyArabic = true; break; }
            if (!anyArabic) return s;

            char[] a = s.ToCharArray();
            System.Array.Reverse(a);
            int i = 0;
            while (i < a.Length)
            {
                if (!IsLtr(a[i]))
                {
                    if (a[i] == '(') a[i] = ')';
                    else if (a[i] == ')') a[i] = '(';
                    i++;
                    continue;
                }
                int j = i, end = i;
                while (j < a.Length && (IsLtr(a[j]) || IsLtrInner(a[j])))
                {
                    if (IsLtr(a[j])) end = j;
                    j++;
                }
                System.Array.Reverse(a, i, end - i + 1);
                i = end + 1;
            }
            return new string(a);
        }
    }
}
