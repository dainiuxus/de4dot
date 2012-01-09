﻿/*
    Copyright (C) 2011 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class ConstantsDecrypter {
		ModuleDefinition module;
		InitializedDataCreator initializedDataCreator;
		TypeDefinition decrypterType;
		MethodDefinition int32Decrypter;
		MethodDefinition int64Decrypter;
		MethodDefinition singleDecrypter;
		MethodDefinition doubleDecrypter;
		MethodDefinition arrayDecrypter;
		EmbeddedResource encryptedResource;
		int[] decryptedInts;
		long[] decryptedLongs;
		float[] decryptedFloats;
		double[] decryptedDoubles;

		public bool Detected {
			get { return decrypterType != null; }
		}

		public bool CanDecrypt {
			get { return encryptedResource != null; }
		}

		public Resource Resource {
			get { return encryptedResource; }
		}

		public TypeDefinition Type {
			get { return decrypterType; }
		}

		public MethodDefinition Int32Decrypter {
			get { return int32Decrypter; }
		}

		public MethodDefinition Int64Decrypter {
			get { return int64Decrypter; }
		}

		public MethodDefinition SingleDecrypter {
			get { return singleDecrypter; }
		}

		public MethodDefinition DoubleDecrypter {
			get { return doubleDecrypter; }
		}

		public MethodDefinition ArrayDecrypter {
			get { return arrayDecrypter; }
		}

		public ConstantsDecrypter(ModuleDefinition module, InitializedDataCreator initializedDataCreator) {
			this.module = module;
			this.initializedDataCreator = initializedDataCreator;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (!isConstantDecrypter(type))
					continue;

				int32Decrypter = DotNetUtils.getMethod(type, "System.Int32", "(System.Int32)");
				int64Decrypter = DotNetUtils.getMethod(type, "System.Int64", "(System.Int32)");
				singleDecrypter = DotNetUtils.getMethod(type, "System.Single", "(System.Int32)");
				doubleDecrypter = DotNetUtils.getMethod(type, "System.Double", "(System.Int32)");
				arrayDecrypter = DotNetUtils.getMethod(type, "System.Array", "(System.Byte[])");
				decrypterType = type;
				return;
			}
		}

		bool isConstantDecrypter(TypeDefinition type) {
			if (type.HasEvents)
				return false;
			if (type.NestedTypes.Count != 1)
				return false;

			var nested = type.NestedTypes[0];
			if (!checkNestedFields(nested))
				return false;

			if (DotNetUtils.getMethod(type, "System.Int32", "(System.Int32)") == null)
				return false;
			if (DotNetUtils.getMethod(type, "System.Int64", "(System.Int32)") == null)
				return false;
			if (DotNetUtils.getMethod(type, "System.Single", "(System.Int32)") == null)
				return false;
			if (DotNetUtils.getMethod(type, "System.Double", "(System.Int32)") == null)
				return false;
			if (DotNetUtils.getMethod(type, "System.Array", "(System.Byte[])") == null)
				return false;

			return true;
		}

		static string[] requiredTypes = new string[] {
			"System.Int32[]",
			"System.Int64[]",
			"System.Single[]",
			"System.Double[]",
		};
		bool checkNestedFields(TypeDefinition nested) {
			if (!new FieldTypes(nested).all(requiredTypes))
				return false;
			foreach (var field in nested.Fields) {
				if (MemberReferenceHelper.compareTypes(nested, field.FieldType))
					return true;
			}
			return false;
		}

		public void initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (decrypterType == null)
				return;

			encryptedResource = findEncryptedResource(simpleDeobfuscator, deob);
			if (encryptedResource == null) {
				Log.w("Could not find encrypted constants resource");
				return;
			}

			var decrypted = new ResourceDecrypter(module).decrypt(encryptedResource.GetResourceData());
			var reader = new BinaryReader(new MemoryStream(decrypted));
			int count;

			count = reader.ReadInt32();
			decryptedInts = new int[count];
			while (count-- > 0)
				decryptedInts[count] = reader.ReadInt32();

			count = reader.ReadInt32();
			decryptedLongs = new long[count];
			while (count-- > 0)
				decryptedLongs[count] = reader.ReadInt64();

			count = reader.ReadInt32();
			decryptedFloats = new float[count];
			while (count-- > 0)
				decryptedFloats[count] = reader.ReadSingle();

			count = reader.ReadInt32();
			decryptedDoubles = new double[count];
			while (count-- > 0)
				decryptedDoubles[count] = reader.ReadDouble();
		}

		EmbeddedResource findEncryptedResource(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			foreach (var method in decrypterType.Methods) {
				if (!DotNetUtils.isMethod(method, "System.String", "()"))
					continue;
				if (!method.IsStatic)
					continue;
				simpleDeobfuscator.deobfuscate(method);
				simpleDeobfuscator.decryptStrings(method, deob);
				foreach (var s in DotNetUtils.getCodeStrings(method)) {
					var resource = DotNetUtils.getResource(module, s) as EmbeddedResource;
					if (resource != null)
						return resource;
				}
			}
			return null;
		}

		public int decryptInt32(int index) {
			return decryptedInts[index];
		}

		public long decryptInt64(int index) {
			return decryptedLongs[index];
		}

		public float decryptSingle(int index) {
			return decryptedFloats[index];
		}

		public double decryptDouble(int index) {
			return decryptedDoubles[index];
		}

		struct ArrayInfo {
			public FieldDefinition encryptedField;
			public ArrayType arrayType;
			public int start, len;

			public ArrayInfo(int start, int len, FieldDefinition encryptedField, ArrayType arrayType) {
				this.start = start;
				this.len = len;
				this.encryptedField = encryptedField;
				this.arrayType = arrayType;
			}
		}

		public void deobfuscate(Blocks blocks) {
			if (arrayDecrypter == null)
				return;

			var infos = new List<ArrayInfo>();
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				infos.Clear();
				for (int i = 0; i < instrs.Count - 6; i++) {
					int index = i;

					var ldci4 = instrs[index++];
					if (!ldci4.isLdcI4())
						continue;

					var newarr = instrs[index++];
					if (newarr.OpCode.Code != Code.Newarr)
						continue;
					if (newarr.Operand == null || newarr.Operand.ToString() != "System.Byte")
						continue;

					if (instrs[index++].OpCode.Code != Code.Dup)
						continue;

					var ldtoken = instrs[index++];
					if (ldtoken.OpCode.Code != Code.Ldtoken)
						continue;
					var field = ldtoken.Operand as FieldDefinition;
					if (field == null)
						continue;

					var call1 = instrs[index++];
					if (call1.OpCode.Code != Code.Call && call1.OpCode.Code != Code.Callvirt)
						continue;
					if (!DotNetUtils.isMethod(call1.Operand as MethodReference, "System.Void", "(System.Array,System.RuntimeFieldHandle)"))
						continue;

					var call2 = instrs[index++];
					if (call2.OpCode.Code != Code.Call && call2.OpCode.Code != Code.Callvirt)
						continue;
					if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(call2.Operand as MethodReference, arrayDecrypter))
						continue;

					var castclass = instrs[index++];
					if (castclass.OpCode.Code != Code.Castclass)
						continue;
					var arrayType = castclass.Operand as ArrayType;
					if (arrayType == null)
						continue;
					if (arrayType.ElementType.PrimitiveSize == -1) {
						Log.w("Can't decrypt non-primitive type array in method {0}", blocks.Method.MetadataToken.ToInt32());
						continue;
					}

					infos.Add(new ArrayInfo(i, index - i, field, arrayType));
				}

				infos.Reverse();
				foreach (var info in infos) {
					var elemSize = info.arrayType.ElementType.PrimitiveSize;
					var decrypted = decryptArray(info.encryptedField.InitialValue, elemSize);

					initializedDataCreator.addInitializeArrayCode(block, info.start, info.len, info.arrayType.ElementType, decrypted);
					Log.v("Decrypted {0} array: {1} elements", info.arrayType.ElementType.ToString(), decrypted.Length / elemSize);
				}
			}
		}

		byte[] decryptArray(byte[] encryptedData, int elemSize) {
			var decrypted = new ResourceDecrypter(module).decrypt(encryptedData);
			var ary = (Array)new BinaryFormatter().Deserialize(new MemoryStream(decrypted));
			if (ary is byte[])
				return (byte[])ary;
			var newAry = new byte[ary.Length * elemSize];
			Buffer.BlockCopy(ary, 0, newAry, 0, newAry.Length);
			return newAry;
		}
	}
}