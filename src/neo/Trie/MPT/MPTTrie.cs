using Neo.IO.Json;
using Neo.Persistence;
using System;
using static Neo.Helper;

namespace Neo.Trie.MPT
{
    public class MPTTrie : MPTReadOnlyTrie
    {
        private MPTDb db;

        public MPTTrie(UInt256 root, ISnapshot store, byte prefix) : base(root, store, prefix)
        {
            this.db = new MPTDb(store, prefix);
        }

        public bool Put(byte[] key, byte[] value)
        {
            var path = key.ToNibbles();
            if (ExtensionNode.MaxKeyLength < path.Length)
                return false;
            if (value.Length == 0)
                return TryDelete(ref root, path);
            var n = new LeafNode(value);
            return Put(ref root, path, n);
        }

        private bool Put(ref MPTNode node, byte[] path, MPTNode val)
        {
            switch (node)
            {
                case LeafNode leafNode:
                    {
                        if (path.Length == 0 && val is LeafNode v)
                        {
                            node = v;
                            db.Put(node);
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
                        if (hashNode.IsEmptyNode)
                        {
                            var newNode = new ExtensionNode()
                            {
                                Key = path,
                                Next = val,
                            };
                            node = newNode;
                            if (!(val is HashNode)) db.Put(val);
                            db.Put(node);
                            return true;
                        }
                        var new_node = Resolve(hashNode);
                        if (new_node is null) return false;
                        return Put(ref node, path, val);
                    }
                default:
                    return false;
            }
        }

        public bool TryDelete(byte[] key)
        {
            var path = key.ToNibbles();
            return TryDelete(ref root, path);
        }

        private bool TryDelete(ref MPTNode node, byte[] path)
        {
            switch (node)
            {
                case LeafNode leafNode:
                    {
                        if (path.Length == 0)
                        {
                            node = HashNode.EmptyNode();
                            return true;
                        }
                        return false;
                    }
                case ExtensionNode extensionNode:
                    {
                        if (path.AsSpan().StartsWith(extensionNode.Key))
                        {
                            var result = TryDelete(ref extensionNode.Next, path[extensionNode.Key.Length..]);
                            if (!result) return false;
                            if (extensionNode.Next is HashNode hashNode && hashNode.IsEmptyNode)
                            {
                                node = extensionNode.Next;
                                return true;
                            }
                            if (extensionNode.Next is ExtensionNode sn)
                            {
                                extensionNode.Key = Concat(extensionNode.Key, sn.Key);
                                extensionNode.Next = sn.Next;
                            }
                            extensionNode.SetDirty();
                            db.Put(extensionNode);
                            return true;
                        }
                        return false;
                    }
                case BranchNode branchNode:
                    {
                        bool result;
                        if (path.Length == 0)
                        {
                            result = TryDelete(ref branchNode.Children[BranchNode.ChildCount - 1], path);
                        }
                        else
                        {
                            result = TryDelete(ref branchNode.Children[path[0]], path[1..]);
                        }
                        if (!result) return false;
                        var childrenIndexes = Array.Empty<byte>();
                        for (int i = 0; i < BranchNode.ChildCount; i++)
                        {
                            if (branchNode.Children[i] is HashNode hn && hn.IsEmptyNode) continue;
                            childrenIndexes = childrenIndexes.Add((byte)i);
                        }
                        if (childrenIndexes.Length > 1)
                        {
                            branchNode.SetDirty();
                            db.Put(branchNode);
                            return true;
                        }
                        var lastChildIndex = childrenIndexes[0];
                        var lastChild = branchNode.Children[lastChildIndex];
                        if (lastChildIndex == BranchNode.ChildCount - 1)
                        {
                            node = lastChild;
                            return true;
                        }
                        if (lastChild is HashNode hashNode)
                        {
                            lastChild = Resolve(hashNode);
                            if (lastChild is null) return false;
                        }
                        if (lastChild is ExtensionNode exNode)
                        {
                            exNode.Key = Concat(childrenIndexes, exNode.Key);
                            exNode.SetDirty();
                            db.Put(exNode);
                            node = exNode;
                            return true;
                        }
                        var newNode = new ExtensionNode()
                        {
                            Key = childrenIndexes,
                            Next = lastChild,
                        };
                        node = newNode;
                        db.Put(node);
                        return true;
                    }
                case HashNode hashNode:
                    {
                        if (hashNode.IsEmptyNode)
                        {
                            return true;
                        }
                        var new_node = Resolve(hashNode);
                        if (new_node is null) return false;
                        node = new_node;
                        return TryDelete(ref node, path);
                    }
                default:
                    return false;
            }
        }

        public JObject ToJson()
        {
            return root.ToJson();
        }
    }
}
