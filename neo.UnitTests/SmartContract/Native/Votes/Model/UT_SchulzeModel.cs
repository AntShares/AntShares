﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.SmartContract.Native;
using System;
using System.Collections.Generic;

namespace Neo.UnitTests.SmartContract.Native
{
    [TestClass]
    public class UT_SchulzeModel
    {
        [TestMethod]
        public void TestCalculateVote()
        {
            List<SchulzeVoteUnit> voteList = new List<SchulzeVoteUnit>();
            voteList.Add(new SchulzeVoteUnit(5,new List<int>()  { 0, 2, 1, 4, 3 }));
            voteList.Add(new SchulzeVoteUnit(5, new List<int>() { 0, 3, 4, 2, 1 }));
            voteList.Add(new SchulzeVoteUnit(8, new List<int>() { 1, 4, 3, 0, 2 }));
            voteList.Add(new SchulzeVoteUnit(3, new List<int>() { 2, 0, 1, 4, 3 }));
            voteList.Add(new SchulzeVoteUnit(7, new List<int>() { 2, 0, 4, 1, 3 }));
            voteList.Add(new SchulzeVoteUnit(2, new List<int>() { 2, 1, 0, 3, 4 }));
            voteList.Add(new SchulzeVoteUnit(7, new List<int>() { 3, 2, 4, 1, 0 }));
            voteList.Add(new SchulzeVoteUnit(8, new List<int>() { 4, 1, 0, 3, 2 }));

            SchulzeModel sMethod = new SchulzeModel();
            int[,] pArray = sMethod.CalculateVote(voteList);
            int[,] expectedArray = new int[,]{
                { -1  ,28   ,28   ,30   ,24 },
                { 25  , -1  ,28   ,33   ,24 },
                { 25  , 29  , -1  ,29   ,24 },
                { 25  , 28  , 28  , -1  ,24 },
                { 25  , 28  , 28  , 31  , -1}
            };
            Assert.AreEqual(String.Join(",",expectedArray), String.Join(",", pArray));
        }
    }
}
