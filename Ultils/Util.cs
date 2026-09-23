using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LightGateway.Ultils
{
    public class Util
    {
        public static byte[] GetBytes(bool value, bool HightFirst)
        {
            return new byte[1] { (byte)(value ? 1u : 0u) };
        }

        public static byte[] GetBytes(char value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static byte[] GetBytes(short value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static byte[] GetBytes(int value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static byte[] GetBytes(long value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static byte[] GetBytes(ushort value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static byte[] GetBytes(uint value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static byte[] GetBytes(ulong value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static byte[] GetBytes(float value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static byte[] GetBytes(double value, bool HightFirst)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static int ByteToShort(byte[] bytes, bool HightFirst, bool havaSig = true)
        {
            if (HightFirst)
            {
                Array.Reverse(bytes);//高位在前
            }

            if (!havaSig)
            {
                return BitConverter.ToUInt16(bytes, 0);
            }

            return BitConverter.ToInt16(bytes, 0);
        }

        public static byte[] RegisterHighLowCHange(byte[] acValue)
        {
            for (int i = 0; i < acValue.Length; i += 2)
            {
                byte b = acValue[i];
                acValue[i] = acValue[i + 1];
                acValue[i + 1] = b;
            }

            return acValue;
        }

        public static int ByteToInt(byte[] bytes, bool HightFirst, bool havaSig = true)
        {
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            if (!havaSig)
            {
                return (int)BitConverter.ToUInt32(bytes, 0);
            }

            return BitConverter.ToInt32(bytes, 0);
        }

        public static long ByteToLong(byte[] bytes, bool HightFirst, bool havaSig = true)
        {
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            long num = bytes[0] & 0xFF;
            for (int i = 1; i < bytes.Length; i++)
            {
                long num2 = bytes[i] & 0xFF;
                num2 <<= 8 * i;
                num |= num2;
            }

            return num;
        }

        public static ulong ByteToULong(byte[] bytes, bool HightFirst)
        {
            if (HightFirst)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToUInt64(bytes, 0);
        }

        public static byte[] ConvertHexStringToBytes(string hexString)
        {
            hexString = hexString.Replace(" ", "");
            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException("参数长度不正确");
            }

            byte[] array = new byte[hexString.Length / 2];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }

            return array;
        }

        public static string ConvertBytesToHexString(byte[] bytes, string SplitStr = null)
        {
            string text = BitConverter.ToString(bytes);
            if (SplitStr != null)
            {
                text = text.Replace("-", SplitStr);
            }

            return text;
        }

        public static int GetIntegerSomeBit(int _Resource, int _Mask)
        {
            return (_Resource >> _Mask) & 1;
        }

        public static ulong ByteToUNumber(byte[] bytes, bool hightFirst, int byteLenght = -1)
        {
            if (byteLenght < 0)
            {
                byteLenght = bytes.Length;
            }

            switch (byteLenght)
            {
                case 2:
                    return (ulong)ByteToShort(bytes, hightFirst, havaSig: false);
                case 4:
                    return (ulong)ByteToInt(bytes, hightFirst, havaSig: false);
                case 8:
                    return ByteToULong(bytes, hightFirst);
                default:
                    return ulong.MaxValue;
            }
        }

        //CRC校验算法
        public static ushort CalculateCrc(byte[] data)
        {
            ushort crc = 0xFFFF;

            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) == 1)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }
    }




}