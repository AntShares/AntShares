using Neo.IO;
using Neo.IO.Json;
using Neo.Persistence;
using System;
using System.Collections.Generic;
using static Neo.Helper;

namespace Neo.Trie.MPT
{
    public partial class MPTTrie<TKey, TValue> : MPTReadOnlyTrie<TKey, TValue>
        where TKey : notnull, ISerializable, new()
        where TValue : class, ISerializable, new()
    {
        public bool Put(TKey key, TValue value)
        {
            var path = key.ToArray().ToNibbles();
            var val = value.ToArray();
            if (ExtensionNode.MaxKeyLength < path.Length || path.Length == 0)
                return false;
            if (LeafNode.MaxValueLength < val.Length)
                return false;
            if (val.Length == 0)
                return TryDelete(ref root, path);
            var n = new LeafNode(val);
            return Put(ref root, path, n);
        }

        private bool Put(ref MPTNode node, byte[] path, MPTNode val)
        {
            switch (node)
            {
                case LeafNode leafNode:
                    {
                        if (val is LeafNode v)
                        {
                            if (path.Length == 0)
                            {
                                node = v;
                                db.Put(node);
                                return true;
                            }
                            var branch = new BranchNode();
                            branch.Children[BranchNode.ChildCount - 1] = leafNode;
                            Put(ref branch.Children[path[0]], path[1..], v);
                            db.Put(branch);
                            node = branch;
                            return true;
                        }
                        return false;
                    }
                case ExtensionNode extensionNode:
                    {
                        if (path.AsSpan().StartsWith(extensionNode.Key))
                        {
                            var result = Put(ref extensionNode.Next, path[extensionNode.Key.Length..], val);
                            if (result)
                            {
                                extensionNode.SetDirty();
                                db.Put(extensionNode);
                            }
                            return result;
                        }
                        var prefix = extensionNode.Key.CommonPrefix(path);
                        var pathRemain = path[prefix.Length..];
                        var keyRemain = extensionNode.Key[prefix.Length..];
                        var son = new BranchNode();
                        MPTNode grandSon1 = HashNode.EmptyNode();
                        MPTNode grandSon2 = HashNode.EmptyNode();

                        Put(ref grandSon1, keyRemain[1..], extensionNode.Next);
                        son.Children[keyRemain[0]] = grandSon1;

                        if (pathRemain.Length == 0)
                        {
                            Put(ref grandSon2, pathRemain, val);
                            son.Children[BranchNode.ChildCount - 1] = grandSon2;
                        }
                        else
                        {
                            Put(ref grandSon2, pathRemain[1..], val);
                            son.Children[pathRemain[0]] = grandSon2;
                        }
                        db.Put(son);
                        if (prefix.Length > 0)
                        {
                            var exNode = new ExtensionNode()
                            {
                                Key = prefix,
                                Next = son,
                            };
                            db.Put(exNode);
                            node = exNode;
                        }
                        else
                        {
                            node = son;
                        }
                        return true;
                    }
                case BranchNode branchNode:
                    {
                        bool result;
                        if (path.Length == 0)
                        {
                            result = Put(ref branchNode.Children[BranchNode.ChildCount - 1], path, val);
                        }
                        else
                        {
                            result = Put(ref branchNode.Children[path[0]], path[1..], val);
                        }
                        if (result)
                        {
                            branchNode.SetDirty();
                            db.Put(branchNode);
                        }
                        return result;
                    }
                case HashNode hashNode:
                    {
                        MPTNode newNode;
                        if (hashNode.IsEmptyNode)
                        {
                            if (path.Length == 0)
                            {
                                newNode = val;
                            }
                            else
                            {
                                newNode = new ExtensionNode()
                                {
                                    Key = path,
                                    Next = val,
                                };
                                db.Put(newNode);
                            }
                            node = newNode;
                            if (val is LeafNode) db.Put(val);
                            return true;
                        }
                        newNode = Resolve(hashNode);
                        if (newNode is null) return false;
                        node = newNode;
                        return Put(ref node, path, val);
                    }
                default:
                    return false;
            }
        }
    }
}
