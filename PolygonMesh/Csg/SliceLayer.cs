﻿/*
Copyright (c) 2015, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using MatterHackers.Agg.VertexSource;
using MatterHackers.VectorMath;

namespace MatterHackers.PolygonMesh.Csg
{
	public static class SliceLayer
	{
		public static List<List<Vector2>> GetPolygonXYLoopsAt0(this Mesh mesh, Matrix4X4 matrix)
		{
			var slicePlane = new Plane(Vector3.UnitZ, 0);

			// transform our plane to the mesh
			var toMeshMatrix = matrix.Inverted;
			var planeInMeshSpace = new Plane(
				Vector3Ex.TransformNormal(slicePlane.Normal, toMeshMatrix),
				Vector3Ex.Transform(slicePlane.Normal * slicePlane.DistanceFromOrigin, toMeshMatrix));

			return CreateSlice(mesh, planeInMeshSpace);
		}

		public static List<List<Vector2>> CreateSlice(Mesh mesh, Plane planeInMeshSpace)
		{
			var n = planeInMeshSpace.Normal;
			var up = new Vector3(n.Y, n.Z, n.X);
			var invFlattenedMatrix = Matrix4X4.LookAt(Vector3.Zero, n, up);
			invFlattenedMatrix *= Matrix4X4.CreateTranslation(n * planeInMeshSpace.DistanceFromOrigin);
			var flattenedMatrix = invFlattenedMatrix.Inverted;

			// collect all the segments this plane intersects and record them in unordered segments in z 0 space
			var meshTo0Plane = flattenedMatrix;
			var unorderedSegments = new List<Segment>(); 
			foreach (var face in mesh.Faces)
			{
				var start = Vector3.Zero;
				var end = Vector3.Zero;
				if (face.GetCutLine(mesh.Vertices, planeInMeshSpace, ref start, ref end))
				{
					var startAtZ0 = Vector3Ex.Transform(start, meshTo0Plane);
					var endAtZ0 = Vector3Ex.Transform(end, meshTo0Plane);
					unorderedSegments.Add(
						new Segment(
							new Vector2(startAtZ0.X, startAtZ0.Y),
							new Vector2(endAtZ0.X, endAtZ0.Y)));
				}
			}

			// connect all the segments together into polygons
			return FindClosedPolygons(unorderedSegments);
		}

		public static List<List<Vector2>> FindClosedPolygons(List<Segment> UnorderedSegments)
		{
			var startIndexes = CreateFastIndexLookup(UnorderedSegments);
			
			var segmentHasBeenAdded = new bool[UnorderedSegments.Count];

			var openPolygonList = new List<List<Vector2>>();
			var closedPolygons = new List<List<Vector2>>();

			for (int startingSegmentIndex = 0; startingSegmentIndex < UnorderedSegments.Count; startingSegmentIndex++)
			{
				if (segmentHasBeenAdded[startingSegmentIndex])
				{
					continue;
				}

				var poly = new List<Vector2>();
				// We start by adding the start, as we will add ends from now on.
				var polygonStartPosition = UnorderedSegments[startingSegmentIndex].Start;
				poly.Add(polygonStartPosition);

				int segmentIndexBeingAdded = startingSegmentIndex;
				bool canClose;

				while (true)
				{
					canClose = false;
					segmentHasBeenAdded[segmentIndexBeingAdded] = true;
					var addedSegmentEndPoint = UnorderedSegments[segmentIndexBeingAdded].End;

					poly.Add(addedSegmentEndPoint);
					segmentIndexBeingAdded = GetTouchingSegmentIndex(UnorderedSegments, startIndexes, segmentHasBeenAdded, addedSegmentEndPoint);
					if (segmentIndexBeingAdded == -1)
					{
						// if we have looped back around to where we started
						if (addedSegmentEndPoint == polygonStartPosition)
						{
							canClose = true;
						}
						break;
					}
					else
					{
						var foundSegmentStart = UnorderedSegments[segmentIndexBeingAdded].Start;
						if (addedSegmentEndPoint == foundSegmentStart)
						{
							// if we have looped back around to where we started
							if (addedSegmentEndPoint == polygonStartPosition)
							{
								canClose = true;
							}
						}
					}
				}

				if (canClose)
				{
					closedPolygons.Add(poly);
				}
				else
				{
					openPolygonList.Add(poly);
				}
			}

			// Remove all polygons from the open polygon list that have 0 points
			for (int i = openPolygonList.Count - 1; i >= 0; i--)
			{
				// add in the position of the last point
				if (openPolygonList[i].Count == 0)
				{
					openPolygonList.RemoveAt(i);
				}
				else // check if every point is the same
				{
					bool allSame = true;
					var first = openPolygonList[i][0];
					for (int j = 1; j < openPolygonList[i].Count; j++)
					{
						if (openPolygonList[i][j] != first)
						{
							allSame = false;
							break;
						}
					}

					if (allSame)
					{
						openPolygonList.RemoveAt(i);
					}
				}
			}

			var startSorter = new SortedVector2();
			for (int i = 0; i < openPolygonList.Count; i++)
			{
				startSorter.Add(i, openPolygonList[i][0]);
			}
			startSorter.Sort();

			var endSorter = new SortedVector2();
			for (int i = 0; i < openPolygonList.Count; i++)
			{
				endSorter.Add(i, openPolygonList[i][openPolygonList[i].Count - 1]);
			}
			endSorter.Sort();

			// Link up all the missing ends, closing up the smallest gaps first. This is an inefficient implementation which can run in O(n*n*n) time.
			while (true)
			{
				double bestScore = double.MaxValue;
				int bestA = -1;
				int bestB = -1;
				bool reversed = false;
				for (int polygonAIndex = 0; polygonAIndex < openPolygonList.Count; polygonAIndex++)
				{
					if (openPolygonList[polygonAIndex].Count < 1)
					{
						continue;
					}

					var aEndPosition = openPolygonList[polygonAIndex][openPolygonList[polygonAIndex].Count - 1];
					// find the closestStartFromEnd
					double distanceToStartSqrd;
					int bStartIndex = startSorter.FindClosetIndex(aEndPosition, out distanceToStartSqrd);
					if (distanceToStartSqrd < bestScore)
					{
						bestScore = distanceToStartSqrd;
						bestA = polygonAIndex;
						bestB = bStartIndex;
						reversed = false;

						if (bestScore == 0)
						{
							// found a perfect match stop looking
							break;
						}
					}

					// find the closestStartFromStart
					double distanceToEndSqrd;
					int bEndIndex = endSorter.FindClosetIndex(aEndPosition, out distanceToEndSqrd, polygonAIndex);
					if (distanceToEndSqrd < bestScore)
					{
						bestScore = distanceToEndSqrd;
						bestA = polygonAIndex;
						bestB = bEndIndex;
						reversed = true;

						if (bestScore == 0)
						{
							// found a perfect match stop looking
							break;
						}
					}

					if (bestScore == 0)
					{
						// found a perfect match stop looking
						break;
					}
				}

				if (bestScore >= double.MaxValue)
				{
					// we could not find any points to connect this to
					break;
				}

				if (bestA == bestB) // This loop connects to itself, close the polygon.
				{
					closedPolygons.Add(new List<Vector2>(openPolygonList[bestA]));
					openPolygonList[bestA].Clear(); // B is cleared as it is A
					endSorter.Remove(bestA);
					startSorter.Remove(bestA);
				}
				else
				{
					if (reversed)
					{
						if (openPolygonList[bestA].Count > openPolygonList[bestB].Count)
						{
							for (int indexB = openPolygonList[bestB].Count - 1; indexB >= 0; indexB--)
							{
								openPolygonList[bestA].Add(openPolygonList[bestB][indexB]);
							}
							openPolygonList[bestB].Clear();
							endSorter.Remove(bestB);
							startSorter.Remove(bestB);
						}
						else
						{
							for (int indexA = openPolygonList[bestA].Count - 1; indexA >= 0; indexA--)
							{
								openPolygonList[bestB].Add(openPolygonList[bestA][indexA]);
							}
							openPolygonList[bestA].Clear();
							endSorter.Remove(bestA);
							startSorter.Remove(bestA);
						}
					}
					else
					{
						openPolygonList[bestA].AddRange(openPolygonList[bestB]);
						openPolygonList[bestB].Clear();
						endSorter.Remove(bestB);
						startSorter.Remove(bestB);
					}
				}
			}

			//Remove all the tiny polygons, or polygons that are not closed. As they do not contribute to the actual print.
			int minimumPerimeter = 1000;
			for (int polygonIndex = 0; polygonIndex < closedPolygons.Count; polygonIndex++)
			{
				double perimeterLength = 0;

				for (int intPointIndex = 1; intPointIndex < closedPolygons[polygonIndex].Count; intPointIndex++)
				{
					perimeterLength += (closedPolygons[polygonIndex][intPointIndex] - closedPolygons[polygonIndex][intPointIndex - 1]).Length;
					if (perimeterLength > minimumPerimeter)
					{
						break;
					}
				}
				if (perimeterLength < minimumPerimeter)
				{
					closedPolygons.RemoveAt(polygonIndex);
					polygonIndex--;
				}
			}

			// TODO: clean up collinear and coincident points
			return closedPolygons;
		}

		public static IEnumerable<VertexData> Vertices()
		{
			throw new System.NotImplementedException();
		}

		private static Dictionary<ulong, List<int>> CreateFastIndexLookup(List<Segment> UnorderedSegments)
		{
			var startIndexes = new Dictionary<ulong, List<int>>();

			for (int startingSegmentIndex = 0; startingSegmentIndex < UnorderedSegments.Count; startingSegmentIndex++)
			{
				ulong positionKey = UnorderedSegments[startingSegmentIndex].Start.GetLongHashCode();
				if (!startIndexes.ContainsKey(positionKey))
				{
					startIndexes.Add(positionKey, new List<int>());
				}

				startIndexes[positionKey].Add(startingSegmentIndex);
			}

			return startIndexes;
		}

		private static int GetTouchingSegmentIndex(List<Segment> UnorderedSegments,
			Dictionary<ulong, List<int>> startIndexes,
			bool[] segmentHasBeenAdded,
			Vector2 addedSegmentEndPoint)
		{
			int lookupSegmentIndex = -1;
			ulong positionKey = addedSegmentEndPoint.GetLongHashCode();
			if (startIndexes.ContainsKey(positionKey))
			{
				foreach (int index in startIndexes[positionKey])
				{
					if (!segmentHasBeenAdded[index])
					{
						if (UnorderedSegments[index].Start == addedSegmentEndPoint)
						{
							lookupSegmentIndex = index;
						}
					}
				}
			}

			return lookupSegmentIndex;
		}
	}
}