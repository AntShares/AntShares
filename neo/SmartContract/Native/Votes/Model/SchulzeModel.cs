﻿using System;
using System.Collections.Generic;
using System.Numerics;
using Neo.SmartContract.Native.Votes;

namespace Neo.SmartContract.Native.Votes.Model
{
    public class SchulzeModel : IMultiVoteModel
    {
        public int[,] CalculateVote(List<CalculatedMultiVote> voteList)
        {
            if (voteList == null || voteList.Count == 0) {
                throw new FormatException();
            }
            int[,] dArray = new int[voteList[0].vote.Count, voteList[0].vote.Count];
            for (int i = 0; i < dArray.GetLength(0); i++)
            {
                for (int j = 0; j < dArray.GetLength(1); j++)
                {
                    foreach (CalculatedMultiVote e in voteList)
                    {
                        List<int> key = e.vote;
                        if (key[i] < key[j])
                        {
                            dArray[i, j] += e.balance;
                        }
                    }
                }
            }
            Dictionary<String, int> vGraphic = ConvertDArrayToVGraphic(dArray);
            List<String> nodesList = new List<String>();
            for (int i = 0; i < voteList[0].vote.Count; i++) {
                nodesList.Add(i.ToString());
            }
            FindAllPathMethod fMethod = new FindAllPathMethod(nodesList, vGraphic);
            List<List<String>> paths = fMethod.FindAllPath();
            int[,] pArray = CreatPArray(nodesList, paths, vGraphic);
            return pArray;
        }

        /// <summary>
        /// Convert DArray to a vector graphic.
        /// DArray ,an array record the number of voters who strictly prefer one candidate to another candidate.
        /// For example: Suppose d[V,W] is the number of voters who strictly prefer candidate V to candidate W. 
        /// </summary>
        /// <param name="dArray">DArray</param>
        /// <returns>vector graphic</returns>
        private Dictionary<String, int> ConvertDArrayToVGraphic(int[,] dArray)
        {
            Dictionary<String, int> vectorGraphic = new Dictionary<String, int>();
            for (int i = 0; i < dArray.GetLength(0); i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (dArray[i, j] > dArray[j, i])
                    {
                        vectorGraphic.Add(i + "," + j, dArray[i, j]);
                    }
                    else if (dArray[i, j] == dArray[j, i])
                    {
                        vectorGraphic.Add(i + "," + j, dArray[i, j]);
                        vectorGraphic.Add(j + "," + i, dArray[j, i]);
                    }
                    else
                    {
                        vectorGraphic.Add(j + "," + i, dArray[j, i]);
                    }
                }
            }
            return vectorGraphic;
        }

        /// <summary>
        /// Creat PArray.
        /// PArray ,an array record voting result.
        /// For example:Candidate D is better than candidate E if and only if p[D,E] > p[E,D]. 
        /// </summary>
        /// <param name="nodelist">candidate set</param>
        /// <param name="paths">paths</param>
        /// <param name="vectorGraphic">vector graphic</param>
        /// <returns>PArray</returns>
        private int[,] CreatPArray(List<String> nodelist, List<List<String>> paths, Dictionary<String, int> vectorGraphic)
        {

            int[,] pArray = new int[nodelist.Count, nodelist.Count];
            for (int i = 0; i < nodelist.Count; i++)
            {
                for (int j = 0; j < nodelist.Count; j++)
                {
                    pArray[i, j] = -1;
                }
            }
            Dictionary<String, int> pathWeights = new Dictionary<String, int>();
            foreach (List<String> path in paths)
            {
                int pathWeight = -1;
                String pathName = path[0] + "," + path[path.Count - 1];
                for (int i = 0; i < path.Count - 1; i++)
                {
                    int weight = vectorGraphic[path[i] + "," + path[i + 1]];
                    if (pathWeight < 0 || pathWeight > weight)
                    {
                        pathWeight = weight;
                    }
                }
                if (pathWeights.ContainsKey(pathName))
                {
                    if (pathWeights[pathName] < pathWeight)
                    {
                        pathWeights[pathName]=pathWeight;
                    }
                }
                else
                {
                    pathWeights.Add(pathName, pathWeight);
                }

            }
            foreach (KeyValuePair<String, int> e in pathWeights)
            {
                String[] temp = e.Key.Split(new char[] { ',' });
                int startNodeIndex = System.Convert.ToInt32(temp[0]);
                int endNodeIndex = System.Convert.ToInt32(temp[1]);
                pArray[startNodeIndex, endNodeIndex] = e.Value;
            }
            return pArray;
        }

        /// <summary>
        /// Print PArray
        /// </summary>
        /// <param name="pArray">PArray</param>
        public void PrintPArray(int[,] pArray)
        {
            Console.WriteLine("pArray:行代表from,列代表to");
            Console.Write("From/to ");
            for (int i = 0; i < pArray.GetLength(0); i++) {
                Console.Write(i + "   ");
            }
            Console.Write("\n");
            for (int i = 0; i < pArray.GetLength(0); i++)
            {
                Console.Write(i + "      ");
                for (int j = 0; j < pArray.GetLength(1); j++)
                {
                    Console.Write(pArray[i,j] > 0 ? pArray[i,j] + "   " : pArray[i,j] + "  ");
                }
                Console.Write("\n");
            }
        }
    }

    public class SchulzeVoteUnit
    {
        public int balance;
        public List<int> vote;

        public SchulzeVoteUnit(int balance, List<int> vote)
        {
            this.balance = balance;
            this.vote = vote;
        }
    }
}
