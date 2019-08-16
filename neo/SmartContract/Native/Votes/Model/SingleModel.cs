﻿using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Neo.SmartContract.Native.Votes.Model
{
    public class SingleModel : ISingleVoteModel
    {
        /// <summary>
        /// Calculate vote result
        /// </summary>
        /// <param name="voteList">vote list</param>
        /// <returns>result</returns>
        public int[] CalculateVote(List<CalculatedSingleVote> voteList)
        {
            Dictionary<int, BigInteger> tempSet = new Dictionary<int, BigInteger>();
            foreach (CalculatedSingleVote unit in voteList)
            {
                if (tempSet.ContainsKey(unit.vote))
                {
                    tempSet[unit.vote] += unit.balance;
                }
                else
                {
                    tempSet.Add(unit.vote, unit.balance);
                }
            }
           int[] result = tempSet.OrderByDescending(p => p.Value).Select(p => p.Key).ToList().ToArray();
            return result;
        }
    }

    public class SingleVoteUnit
    {
        public BigInteger balance;
        public int vote;

        public SingleVoteUnit(BigInteger balance, int vote)
        {
            this.balance = balance;
            this.vote = vote;
        }
    }
}

