﻿// Copyright 2006 Herre Kuijpers - <herre@xs4all.nl>
//
// This source file(s) may be redistributed, altered and customized
// by any means PROVIDING the authors name and all copyright
// notices remain intact.
// THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED. USE IT AT YOUR OWN RISK. THE AUTHOR ACCEPTS NO
// LIABILITY FOR ANY DATA DAMAGE/LOSS THAT THIS PRODUCT MAY CAUSE.
//-----------------------------------------------------------------------
/*
Copyright (c) 2014, Lars Brubaker
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
using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.DataConverters3D;
using MatterHackers.ImageProcessing;
using MatterHackers.PolygonMesh;
using MatterHackers.VectorMath;

namespace MatterHackers.RayTracer
{
	public enum RenderType { NONE, ORTHOGROPHIC, PERSPECTIVE, RAY_TRACE };

	public static class ThumbnailEngine
	{
		public static ImageBuffer Generate(IObject3D loadedItem, RenderType renderType, int width, int height, bool allowMultiThreading = true)
		{
			switch (renderType)
			{
				case RenderType.RAY_TRACE:
					{
						loadedItem.Mesh?.Triangulate();

						var tracer = new ThumbnailTracer(loadedItem, width, height)
						{
							MultiThreaded = allowMultiThreading
						};
						tracer.TraceScene();

						tracer.destImage?.SetRecieveBlender(new BlenderPreMultBGRA());

						return tracer.destImage;
					}

				case RenderType.PERSPECTIVE:
					{
						var tracer = new ThumbnailTracer(loadedItem, width, height);

						var thumbnail = new ImageBuffer(width, height);
						var graphics2D = thumbnail.NewGraphics2D();

						foreach (IObject3D item in loadedItem.Children)
						{
							double minZ = double.MaxValue;
							double maxZ = double.MinValue;

							tracer.GetMinMaxZ(item.Mesh, ref minZ, ref maxZ);

							tracer.RenderPerspective(graphics2D, item.Mesh, RGBA_Bytes.White, minZ, maxZ);
						}

						thumbnail.SetRecieveBlender(new BlenderPreMultBGRA());
						return thumbnail;
					}

				case RenderType.NONE:
				case RenderType.ORTHOGROPHIC:
				default:
					{
						var thumbnail = BuildImageFromMeshGroups(loadedItem, width, height);
						
						// Force to all white and return
						return thumbnail.AllWhite();
					}
			}
		}

		private static ImageBuffer BuildImageFromMeshGroups(IObject3D loadedItem, int width, int height, bool debugNonManifoldEdges = false)
		{
			List<MeshGroup> loadedMeshGroups = loadedItem.ToMeshGroupList();

			if (loadedMeshGroups?.Count > 0
				&& loadedMeshGroups[0].Meshes?.Count > 0)
			{
				var tempImage = new ImageBuffer(width, height);
				Graphics2D partGraphics2D = tempImage.NewGraphics2D();
				partGraphics2D.Clear(new RGBA_Bytes());

				AxisAlignedBoundingBox aabb = loadedMeshGroups[0].GetAxisAlignedBoundingBox();

				for (int meshGroupIndex = 1; meshGroupIndex < loadedMeshGroups.Count; meshGroupIndex++)
				{
					aabb = AxisAlignedBoundingBox.Union(aabb, loadedMeshGroups[meshGroupIndex].GetAxisAlignedBoundingBox());
				}

				double maxSize = Math.Max(aabb.XSize, aabb.YSize);
				double scale = width / (maxSize * 1.2);

				var bounds2D = new RectangleDouble(aabb.minXYZ.x, aabb.minXYZ.y, aabb.maxXYZ.x, aabb.maxXYZ.y);
				foreach (MeshGroup meshGroup in loadedMeshGroups)
				{
					foreach (Mesh loadedMesh in meshGroup.Meshes)
					{
						PolygonMesh.Rendering.OrthographicZProjection.DrawTo(
							partGraphics2D,
							loadedMesh,
							new Vector2(
								(width / scale - bounds2D.Width) / 2 - bounds2D.Left,
								(height / scale - bounds2D.Height) / 2 - bounds2D.Bottom),
							scale,
							RGBA_Bytes.White);
					}
				}

				if (debugNonManifoldEdges)
				{
					foreach (Mesh loadedMesh in loadedMeshGroups[0].Meshes)
					{
						List<MeshEdge> nonManifoldEdges = loadedMesh.GetNonManifoldEdges();
						if (nonManifoldEdges.Count > 0)
						{
							partGraphics2D.Circle(width / 4, width / 4, width / 8, RGBA_Bytes.Red);
						}
					}
				}

				// Force to all white and return
				return tempImage;
			}

			return null;
		}
	}
}