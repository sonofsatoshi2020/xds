﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin.Crypto;

namespace NBitcoin
{
    public class MerkleNode
    {
        public MerkleNode(uint256 hash)
        {
            this.Hash = hash;
            this.IsLeaf = true;
        }

        public MerkleNode(MerkleNode left, MerkleNode right)
        {
            this.Left = left;
            this.Right = right;
            if (left != null)
                left.Parent = this;
            if (right != null)
                right.Parent = this;
            UpdateHash();
        }

        public uint256 Hash { get; set; }

        public bool IsLeaf { get; }

        public MerkleNode Parent { get; private set; }

        public MerkleNode Left { get; }

        public MerkleNode Right { get; }


        internal bool IsMarked { get; set; }

        public static MerkleNode GetRoot(IEnumerable<uint256> leafs)
        {
            var row = leafs.Select(l => new MerkleNode(l)).ToList();
            if (row.Count == 0)
                return new MerkleNode(uint256.Zero);
            while (row.Count != 1)
            {
                var parentRow = new List<MerkleNode>();
                for (var i = 0; i < row.Count; i += 2)
                {
                    var left = row[i];
                    var right = i + 1 < row.Count ? row[i + 1] : null;
                    var parent = new MerkleNode(left, right);
                    parentRow.Add(parent);
                }

                row = parentRow;
            }

            return row[0];
        }

        public static MerkleNode GetRoot(int leafCount)
        {
            if (leafCount > 1024 * 1024)
                throw new ArgumentOutOfRangeException("leafCount",
                    "To prevent DDOS attacks, NBitcoin does not support more than 1024*1024 transactions for the creation of a MerkleNode, if this case is legitimate, contact us.");
            return GetRoot(Enumerable.Range(0, leafCount).Select(i => null as uint256));
        }

        public void UpdateHash()
        {
            var right = this.Right ?? this.Left;
            if (this.Left != null && this.Left.Hash != null && right.Hash != null)
                this.Hash = Hashes.Hash256(this.Left.Hash.ToBytes().Concat(right.Hash.ToBytes()).ToArray());
        }

        public IEnumerable<MerkleNode> EnumerateDescendants()
        {
            IEnumerable<MerkleNode> result = new[] {this};
            if (this.Right != null)
                result = this.Right.EnumerateDescendants().Concat(result);
            if (this.Left != null)
                result = this.Left.EnumerateDescendants().Concat(result);
            return result;
        }

        public MerkleNode GetLeaf(int i)
        {
            return GetLeafs().Skip(i).FirstOrDefault();
        }

        public IEnumerable<MerkleNode> GetLeafs()
        {
            return EnumerateDescendants().Where(l => l.IsLeaf);
        }

        public IEnumerable<MerkleNode> Ancestors()
        {
            var n = this.Parent;
            while (n != null)
            {
                yield return n;
                n = n.Parent;
            }
        }

        public override string ToString()
        {
            return this.Hash == null ? "???" : this.Hash.ToString();
        }

        public string ToString(bool hierachy)
        {
            if (!hierachy)
                return ToString();
            var builder = new StringBuilder();
            ToString(builder, 0);
            return builder.ToString();
        }

        void ToString(StringBuilder builder, int indent)
        {
            var tabs = new string(Enumerable.Range(0, indent).Select(_ => '\t').ToArray());
            builder.Append(tabs);
            builder.AppendLine(ToString());
            if (this.Left != null) this.Left.ToString(builder, indent + 1);
            if (this.Right != null) this.Right.ToString(builder, indent + 1);
        }
    }
}