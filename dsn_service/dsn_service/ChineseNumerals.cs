/**
 * Created by Annon Tan on 2021/8/12
 * From <https://github.com/seahore/ChineseNumerals/blob/main/ChineseNumerals.cs>
 * Edited by SwimmingTiger on 2022/7/8
 */

using System;
using System.Collections.Generic;

namespace DSN {
    class ChineseNumerals {
        static char[] fractionalNum = { '零', '一', '二', '三', '四', '五', '六', '七', '八', '九' };
        static char[] lowerNum = { '\0', '一', '二', '三', '四', '五', '六', '七', '八', '九' };
        static char[] upperNumSimp = { '\0', '壹', '贰', '叁', '肆', '伍', '陆', '柒', '捌', '玖' };
        static char[] upperNumTrad = { '\0', '壹', '貳', '叄', '肆', '伍', '陸', '柒', '捌', '玖' };
        static Dictionary<int, char> lowerLittleSep = new Dictionary<int, char> { { 1000, '千' }, { 100, '百' }, { 10, '十' } };
        static Dictionary<int, char> upperLittleSep = new Dictionary<int, char> { { 1000, '仟' }, { 100, '佰' }, { 10, '拾' } };
        static char[] lowerBigSepSimp = { '\0', '万', '亿', '兆', '京', '垓' };
        static char[] lowerBigSepTrad = { '\0', '萬', '億', '兆', '京', '垓' };

        static string TenThousand2Chinese(int src, bool useLiang, bool ignoreOneBeforeTen, bool upper, bool traditional) {
            int t = src;
            string s = "";
            int i;
            for (i = 1000; t / i == 0; t %= i, i /= 10) ;
            for (; t > 0; t %= i, i /= 10) {
                if (t / i == 0) {
                    for (; t / i == 0; t %= i, i /= 10) ;
                    if (i >= 1) {
                        s += '零';
                    }
                }
                if (useLiang && !upper && t / i == 2 && i >= 100) {
                    s += traditional ? '兩' : '两';
                } else if (!ignoreOneBeforeTen || t / i != 1 || i != 10) {
                    s += upper ? (traditional ? upperNumTrad[t / i] : upperNumSimp[t / i]) : lowerNum[t / i];
                }
                if (i >= 10)
                    s += upper ? upperLittleSep[i] : lowerLittleSep[i];
            }
            return s;
        }

        public static string Number2ChineseJSGF(string str) {
            str = Number2Chinese(str)
                .Replace("一十", "[一]十")
                .Replace("二", "(二|两)")
                .Replace("正", "(正|加)")
                .Replace("负", "(负|减)");
            return str;
        }

        public static string Number2Chinese(string src) {
            if (src.Length > 0 && src[0] == '+') {
                return "正" + Number2Chinese(src.Substring(1));
            }
            if (src.Contains(".")) {
                var parts = src.Split('.');
                if (parts.Length == 2) {
                    // 恰好两段，小数
                    parts[0] = Int2Chinese(Convert.ToInt32(parts[0]));
                    parts[1] = fractionalPart2Chinese(parts[1]);
                } else {
                    // 多于两段，可能是版本号
                    for (int i = 0; i < parts.Length; i++) {
                        parts[i] = fractionalPart2Chinese(parts[i]);
                    }
                }
                return string.Join("点", parts);
            } else {
                return Int2Chinese(Convert.ToInt32(src));
            }
        }

        // 小数部分转中文
        private static string fractionalPart2Chinese(string src) {
            var dst = new char[src.Length];
            for (int i = 0; i < src.Length; i++) {
                dst[i] = fractionalNum[src[i] - 48]; // 0 的 ASCII 码是 48
            }
            return new string(dst);
        }

        /// <summary>
        /// 将int类型数值转换成汉语数字字符串。
        /// </summary>
        /// <param name="src">数值</param>
        /// <param name="useLiang">是否在习惯使用“两”的场合使用“两”</param>
        /// <param name="ignoreOneBeforeTen">是否忽略“十”前的“一”</param>
        /// <param name="upper">是否使用大写数字</param>
        /// <param name="traditional">是否输出繁体</param>
        /// <returns>相应的汉语数字字符串</returns>
        public static string Int2Chinese(int src, bool useLiang = false, bool ignoreOneBeforeTen = false, bool upper = false, bool traditional = false) {
            if (src == 0)
                return "零";
            bool neg = false;
            if (src < 0) {
                neg = true;
                src = -src;
            }
            if (src < 10000)
                return TenThousand2Chinese(src, useLiang, ignoreOneBeforeTen, upper, traditional);

            List<int> l = new List<int>();
            while (src > 0) {
                l.Add(src % 10000);
                src /= 10000;
            }
            string result = "";
            for (int i = 0; i < l.Count - 1; ++i) {
                if (l[i] == 0) {
                    if (i > 0 && l[i - 1] / 1000 != 0) {
                        result = "零" + result;
                    }
                    continue;
                }
                if (i > 0) {
                    if (l[i] == 2) {
                        result = (traditional ? "兩" + lowerBigSepTrad[i] : "两" + lowerBigSepSimp[i]) + result;
                        continue;
                    }
                    result = TenThousand2Chinese(l[i], useLiang, ignoreOneBeforeTen, upper, traditional) + (traditional ? lowerBigSepTrad[i] : lowerBigSepSimp[i]) + result;
                } else {
                    result = TenThousand2Chinese(l[i], useLiang, ignoreOneBeforeTen, upper, traditional);
                }
                if (l[i] < 1000) result = "零" + result;
            }
            if (l[l.Count - 1] == 2) {
                result = (traditional ? "兩" + lowerBigSepTrad[l.Count - 1] : "两" + lowerBigSepSimp[l.Count - 1]) + result;
            } else {
                result = TenThousand2Chinese(l[l.Count - 1], useLiang, ignoreOneBeforeTen, upper, traditional) + (traditional ? lowerBigSepTrad[l.Count - 1] : lowerBigSepSimp[l.Count - 1]) + result;
            }
            if (neg) result = (traditional ? '負' : '负') + result;
            return result;
        }
    }
}
