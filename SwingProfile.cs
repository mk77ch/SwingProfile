#region Using declarations
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.Infinity
{
	public class SwingProfile : Indicator
	{
		#region BarItem
		
		public class BarItem
		{
			public int    idx = 0;
			public double min = 0.0;
			public double max = 0.0;
			public double vol = 0.0;
			
			public ConcurrentDictionary<double, double> lst = new ConcurrentDictionary<double, double>();
			
			public BarItem(int idx)
			{
				this.idx = idx;
			}
		}
		
		#endregion
		
		#region Profile
		
		public class Profile
		{
			public int    dir = 0;
			public int    fst = 0;
			public int    lst = 0;
			public double min = 0.0;
			public double max = 0.0;
			public double poc = 0.0;
			public bool   log = false;
			
			public ConcurrentDictionary<double, double> pro = new ConcurrentDictionary<double, double>();
			
			public Profile(int dir, int fst, int lst, bool log)
			{
				this.dir = dir;
				this.fst = fst;
				this.lst = lst;
				this.log = log;
			}
			
			public void calc()
			{
				this.setRng();
				this.setPoc();
			}
			
			// setRng
			//
			public void setRng()
			{
				try
				{
					if(!this.pro.IsEmpty)
					{
						this.min = this.pro.Keys.Min();
						this.max = this.pro.Keys.Max();
					}
				}
				catch(Exception exception)
				{
					if(this.log)
					{
						NinjaTrader.Code.Output.Process(exception.ToString(), PrintTo.OutputTab1);
					}
				}
			}
			
			// setPoc
			//
			public void setPoc()
			{
				try
				{
					if(!this.pro.IsEmpty)
					{
						this.poc = this.pro.Keys.Aggregate((i, j) => this.pro[i] > this.pro[j] ? i : j);
					}
				}
				catch(Exception exception)
				{
					if(this.log)
					{
						NinjaTrader.Code.Output.Process(exception.ToString(), PrintTo.OutputTab1);
					}
				}
			}
		}
		
		#endregion
		
		#region Variables
		
		private Series<BarItem> BarItems;
		private Series<double>  ZigZagLo;
		private Series<double>  ZigZagHi;
		
		private List<Profile> Profiles;
		
		private int    zzDir 	 = 0;
		private int    lastLoBar = 0;
		private int    lastHiBar = 0;
		private double lastLoVal = 0.0;
		private double lastHiVal = 0.0;
		
		private int    currZzBar = 0;
		private double currZzVal = 0.0;
		private int    prevZzBar = 0;
		private double prevZzVal = 0.0;
		private int    lastZzBar = 0;
		private double lastZzVal = 0.0;
		
		private double dynFontSize = 0.0;
		
		private bool logErrors = true;
		
		#endregion
		
		#region OnStateChange
		
		// OnStateChange
		//
		protected override void OnStateChange()
		{
			if(State == State.SetDefaults)
			{
				Description					= @"";
				Name						= "SwingProfile";
				Calculate					= Calculate.OnEachTick;
				IsOverlay					= true;
				IsAutoScale					= false;
				PaintPriceMarkers			= false;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				ScaleJustification			= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive	= false;
				
				// ---
				
				zzSpan 		 = 5;
				displayType  = SwingProfileDisplayType.Profile;
				upSwingColor = Brushes.LightGreen;
				dnSwingColor = Brushes.Coral;
				maxOpacity   = 0.6f;
				drawBorder   = true;
				drawVolume   = true;
				drawText     = false;
				textColor    = Brushes.DimGray;
				
				AddPlot(new Stroke(Brushes.Transparent, 3f), PlotStyle.Dot, "ZigZagDots");
			}
			else if(State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Tick, 1);
				
				if(ChartBars != null)
				{
					ZOrder = ChartBars.ZOrder - 1;
				}
			}
			else if(State == State.DataLoaded)
			{
				BarItems = new Series<BarItem>(this, MaximumBarsLookBack.Infinite);
				
				ZigZagLo = new Series<double>(this);
				ZigZagHi = new Series<double>(this);
				
				Profiles = new List<Profile>();
			}
			else if(State == State.Terminated)
			{
				if(Profiles != null)
				{
					Profiles.Clear();
				}
			}
		}
		
		#endregion
		
		#region OnBarUpdate
		
		protected override void OnBarUpdate()
		{
			if(CurrentBars[0] < 0) { return; }
			
			if(!Bars.IsTickReplay)
			{
				Draw.TextFixed(this, "noTickReplay", "Please enable Tick Replay...", TextPosition.Center);
				return;
			}
			
			if(BarsInProgress == 0)
			{
				#region ZigZag
				
				if(CurrentBar == 0)
				{
					lastLoVal = Low[0];
					lastHiVal = High[0];
				}
				else
				{
					ZigZagLo[0] = MIN(Low, zzSpan)[0];
					ZigZagHi[0] = MAX(High, zzSpan)[0];
					
					if(zzDir == 0)
					{
						if(ZigZagLo[0] < lastLoVal)
						{
							lastLoVal = ZigZagLo[0];
							lastLoBar = CurrentBar;
							
							if(ZigZagHi[0] < ZigZagHi[1])
							{
								zzDir = -1;
							}
						}
						if(ZigZagHi[0] > lastHiVal)
						{
							lastHiVal = ZigZagHi[0];
							lastHiBar = CurrentBar;
							
							if(ZigZagLo[0] > ZigZagLo[1])
							{
								zzDir = 1;
							}
						}
					}
					
					if(zzDir > 0)
					{
						if(ZigZagHi[0] > lastHiVal)
						{
							lastHiVal = ZigZagHi[0];
							lastHiBar = CurrentBar;
							
							if(Plots[0].Brush != Brushes.Transparent)
							{
								Draw.Line(this, lastLoBar.ToString(), CurrentBar-lastLoBar, lastLoVal, CurrentBar-lastHiBar, lastHiVal, Plots[0].Brush);
							}
						}
						else if(ZigZagHi[0] < lastHiVal && ZigZagLo[0] < ZigZagLo[1])
						{
							if(Plots[0].Brush != Brushes.Transparent)
							{
								Draw.Line(this, lastLoBar.ToString(), CurrentBar-lastLoBar, lastLoVal, CurrentBar-lastHiBar, lastHiVal, Plots[0].Brush);
							}
							
							zzDir     = -1;
							lastLoVal = ZigZagLo[0];
							lastLoBar = CurrentBar;
							
							if(Plots[0].Brush != Brushes.Transparent)
							{
								Draw.Line(this, lastHiBar.ToString(), CurrentBar-lastHiBar, lastHiVal, CurrentBar-lastLoBar, lastLoVal, Plots[0].Brush);
							}
							
							ZigZagDots[CurrentBar-lastHiBar] = lastHiVal;
						}
					}
					else
					{
						if(ZigZagLo[0] < lastLoVal)
						{
							lastLoVal = ZigZagLo[0];
							lastLoBar = CurrentBar;
							
							if(Plots[0].Brush != Brushes.Transparent)
							{
								Draw.Line(this, lastHiBar.ToString(), CurrentBar-lastHiBar, lastHiVal, CurrentBar-lastLoBar, lastLoVal, Plots[0].Brush);
							}
						}
						else if(ZigZagLo[0] > lastLoVal && ZigZagHi[0] > ZigZagHi[1])
						{
							if(Plots[0].Brush != Brushes.Transparent)
							{
								Draw.Line(this, lastHiBar.ToString(), CurrentBar-lastHiBar, lastHiVal, CurrentBar-lastLoBar, lastLoVal, Plots[0].Brush);
							}
							
							zzDir     = 1;
							lastHiVal = ZigZagHi[0];
							lastHiBar = CurrentBar;
							
							if(Plots[0].Brush != Brushes.Transparent)
							{
								Draw.Line(this, lastLoBar.ToString(), CurrentBar-lastLoBar, lastLoVal, CurrentBar-lastHiBar, lastHiVal, Plots[0].Brush);
							}
							
							ZigZagDots[CurrentBar-lastLoBar] = lastLoVal;
						}
					}
					
					int found = 0;
					
					for(int i=CurrentBar;i>0;i--)
					{
						if(ZigZagDots.IsValidDataPointAt(i))
						{
							if(found == 0)
							{
								currZzBar = i;
								currZzVal = ZigZagDots.GetValueAt(i);
								
								found++;
								continue;
							}
							
							if(found == 1)
							{
								prevZzBar = i;
								prevZzVal = ZigZagDots.GetValueAt(i);
								
								break;
							}
						}
					}
					
					if(currZzBar != lastZzBar)
					{
						if(Profiles.Count > 0)
						{
							updateProfile(Profiles.Count-1, prevZzBar, currZzBar);
						}
						
						Profiles.Add(new Profile(zzDir, currZzBar, CurrentBar, logErrors));
						
						updateProfile(Profiles.Count-1, currZzBar, CurrentBar);
					}
					
					lastZzBar = currZzBar;
					lastZzVal = currZzVal;
				}
				
				#endregion
				
				if(BarItems[0] == null)
				{
					BarItems[0] = new BarItem(CurrentBar);
					
					BarItems[0].min = Low[0];
					BarItems[0].max = High[0];
				}
			}
			
			if(BarsInProgress == 1)
			{
				BarItems[0].min  = (Close[0] < BarItems[0].min) ? Close[0] : BarItems[0].min;
				BarItems[0].max  = (Close[0] > BarItems[0].max) ? Close[0] : BarItems[0].max;
				BarItems[0].vol += Volume[0];
				
				if(!BarItems[0].lst.ContainsKey(Close[0]))
				{
					BarItems[0].lst.TryAdd(Close[0], Volume[0]);
				}
				else
				{
					BarItems[0].lst[Close[0]] += Volume[0];
				}
				
				if(Profiles.Count > 0)
				{
					int idx = Profiles.Count-1;
					
					Profiles[idx].lst = CurrentBars[0];
					
					if(!Profiles[idx].pro.ContainsKey(Close[0]))
					{
						Profiles[idx].pro.TryAdd(Close[0], Volume[0]);
					}
					else
					{
						Profiles[idx].pro[Close[0]] += Volume[0];
					}
					
					if(State == State.Realtime || State == State.Historical && CurrentBars[0] == ChartBars.Count - 1)
					{
						Profiles[idx].calc();
					}
				}
			}
		}
		
		#endregion
		
		#region updateProfile
		
		// updateProfile
		//
		private void updateProfile(int idx, int fst, int lst)
		{
			if(Profiles.Count > Math.Max(0, idx))
			{
				Profiles[idx].fst = fst;
				Profiles[idx].lst = lst;
				
				Profiles[idx].pro.Clear();
				
				for(int i=fst;i<=lst;i++)
				{
					if(BarItems[CurrentBars[0]-i] == null || BarItems[CurrentBars[0]-i].lst.IsEmpty)
					{
						continue;
					}
					
					foreach(KeyValuePair<double, double> rd in BarItems[CurrentBars[0]-i].lst)
					{
						Profiles[idx].pro.AddOrUpdate(rd.Key, rd.Value, (key, val) => rd.Value);
					}
				}
				
				Profiles[idx].calc();
			}
		}
		
		#endregion
		
		#region OnRender
		
		// OnRender
		//
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if(Bars == null || Bars.Instrument == null || IsInHitTest) { return; }
			
			base.OnRender(chartControl, chartScale);
			
			try
			{
				dynFontSize = (drawVolume) ? getTextSize(chartScale) : ChartControl.Properties.LabelFont.Size;
				drawSwingProfile(chartControl, chartScale);
			}
			catch(Exception exception)
			{
				if(logErrors)
				{
					NinjaTrader.Code.Output.Process(exception.ToString(), PrintTo.OutputTab1);
				}
			}
		}
		
		#endregion
		
		#region drawSwingProfile
		
		// drawSwingProfile
		//
		private void drawSwingProfile(ChartControl chartControl, ChartScale chartScale)
		{
			try
			{
				if(Profiles.Count == 0) { return; }
				
				SharpDX.Direct2D1.AntialiasMode oldAntialiasMode = RenderTarget.AntialiasMode;
				RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
				
				ChartControlProperties chartProps = chartControl.Properties;
				
				SharpDX.Direct2D1.Brush bckBrush = chartProps.ChartBackground.ToDxBrush(RenderTarget);
				SharpDX.Direct2D1.Brush upsBrush = upSwingColor.ToDxBrush(RenderTarget);
				SharpDX.Direct2D1.Brush dnsBrush = dnSwingColor.ToDxBrush(RenderTarget);
				SharpDX.Direct2D1.Brush txtBrush = textColor.ToDxBrush(RenderTarget);
				
				SharpDX.RectangleF rect = new SharpDX.RectangleF();
				SharpDX.Vector2    vect = new SharpDX.Vector2();
				
				SimpleFont sfDynNorm = new SimpleFont("Consolas", dynFontSize);
				SimpleFont sfDynBold = new SimpleFont("Consolas", dynFontSize){ Bold = true };
				
				TextFormat tfDynNorm = sfDynNorm.ToDirectWriteTextFormat();
				TextFormat tfDynBold = sfDynBold.ToDirectWriteTextFormat();
				TextLayout tl;
				
				float x1,x2,y1,y2,mw,wd,mh,ht = 0f;
				
				double rngMin = chartScale.MinValue;
				double rngMax = chartScale.MaxValue;
				
				for(int i=Profiles.Count-1;i>0;i--)
				{
					if(Profiles[i] == null) { continue; }
					if(Profiles[i].pro.IsEmpty) { continue; }
					if(ChartBars.ToIndex < Profiles[i].fst) { continue; }
					if(Profiles[i].lst < ChartBars.FromIndex) { break; }
					
					double pocPrc = Profiles[i].poc;
					double pocVol = Profiles[i].pro.ContainsKey(pocPrc) ? Profiles[i].pro[pocPrc] : 0.0;
					
					#region profile
					
					if(displayType == SwingProfileDisplayType.Profile)
					{
						x1 = chartControl.GetXByBarIndex(ChartBars, Profiles[i].fst);
						x2 = chartControl.GetXByBarIndex(ChartBars, Profiles[i].lst);
						x2 = (extend && ChartBars.ToIndex > Profiles[i].fst && ChartBars.ToIndex <= Profiles[i].lst) ? chartControl.CanvasRight : x2;
						mw = Math.Abs(x2 - x1);
						mh = Math.Abs(((chartScale.GetYByValue(Profiles[i].max) + chartScale.GetYByValue(Profiles[i].max + TickSize)) / 2) - ((chartScale.GetYByValue(Profiles[i].min) + chartScale.GetYByValue(Profiles[i].min - TickSize)) / 2));
						
						foreach(KeyValuePair<double, double> rd in Profiles[i].pro)
						{
							if(rd.Key < rngMin || rd.Key > rngMax) { continue; }
							
							wd = (float)((mw / pocVol) * rd.Value);
							
							y1 = ((chartScale.GetYByValue(rd.Key) + chartScale.GetYByValue(rd.Key + TickSize)) / 2);
							y2 = ((chartScale.GetYByValue(rd.Key) + chartScale.GetYByValue(rd.Key - TickSize)) / 2);
							
							ht = Math.Abs(y2 - y1);
							
							rect.X      = (float)x1;
							rect.Y      = (float)y1;
							rect.Width  = (float)wd;
							rect.Height = (float)ht;
							
							if((rngMax - rngMin) / TickSize < 100)
							{
								bckBrush.Opacity = 1.0f;
								
								if(wd >= 1f)
								{
									RenderTarget.DrawRectangle(rect, bckBrush);
								}
								
								rect.Width  -= 1f;
								rect.Height -= 1f;
							}
						
							if(Profiles[i].dir > 0)
							{
								upsBrush.Opacity = (rd.Key == Profiles[i].poc) ? maxOpacity + 0.3f : (float)Math.Round((maxOpacity / pocVol) * rd.Value, 5);
								RenderTarget.FillRectangle(rect, upsBrush);
							}
							else
							{
								dnsBrush.Opacity = (rd.Key == Profiles[i].poc) ? maxOpacity + 0.3f : (float)Math.Round((maxOpacity / pocVol) * rd.Value, 5);
								RenderTarget.FillRectangle(rect, dnsBrush);
							}
							
							if(drawText)
							{
								rect.Width = 100f;
							
								tfDynNorm.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
								tfDynBold.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
								
								tl = (rd.Key == Profiles[i].poc) ? new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, rd.Value.ToString(), tfDynBold, rect.Width, rect.Height) : new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, rd.Value.ToString(), tfDynNorm, rect.Width, rect.Height);
								
								tl.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
								
								vect.X = rect.X + 2f;
								vect.Y = rect.Y;
								
								if(Profiles[i].dir > 0)
								{
									RenderTarget.DrawTextLayout(vect, tl, txtBrush);
								}
								else
								{
									RenderTarget.DrawTextLayout(vect, tl, txtBrush);
								}
								
								tl.Dispose();
							}
						}
						
						if(drawBorder)
						{
							rect.X      = (float)x1;
							rect.Y      = (float)((chartScale.GetYByValue(Profiles[i].max) + chartScale.GetYByValue(Profiles[i].max + TickSize)) / 2);
							rect.Width  = (float)mw;
							rect.Height = (float)mh;
							
							if(Profiles[i].dir > 0)
							{
								upsBrush.Opacity = maxOpacity;
								RenderTarget.DrawRectangle(rect, upsBrush);
							}
							else
							{
								dnsBrush.Opacity = maxOpacity;
								RenderTarget.DrawRectangle(rect, dnsBrush);
							}
						}
					}
					
					#endregion
					
					#region map
					
					if(displayType == SwingProfileDisplayType.Map)
					{
						x1 = chartControl.GetXByBarIndex(ChartBars, Profiles[i].fst);
						x2 = chartControl.GetXByBarIndex(ChartBars, Profiles[i].lst);
						x2 = (extend && ChartBars.ToIndex > Profiles[i].fst && ChartBars.ToIndex <= Profiles[i].lst) ? chartControl.CanvasRight : x2;
						wd = Math.Abs(x2 - x1);
						mh = Math.Abs(((chartScale.GetYByValue(Profiles[i].max) + chartScale.GetYByValue(Profiles[i].max + TickSize)) / 2) - ((chartScale.GetYByValue(Profiles[i].min) + chartScale.GetYByValue(Profiles[i].min - TickSize)) / 2));
						
						foreach(KeyValuePair<double, double> rd in Profiles[i].pro)
						{
							if(rd.Key < rngMin || rd.Key > rngMax) { continue; }
							
							y1 = ((chartScale.GetYByValue(rd.Key) + chartScale.GetYByValue(rd.Key + TickSize)) / 2);
							y2 = ((chartScale.GetYByValue(rd.Key) + chartScale.GetYByValue(rd.Key - TickSize)) / 2);
							
							ht = Math.Abs(y2 - y1);
							
							rect.X      = (float)x1;
							rect.Y      = (float)y1;
							rect.Width  = (float)wd;
							rect.Height = (float)ht;
							
							if(Profiles[i].dir > 0)
							{
								upsBrush.Opacity = (rd.Key == Profiles[i].poc) ? maxOpacity + 0.3f : (float)Math.Round((maxOpacity / pocVol) * rd.Value, 5);
								RenderTarget.FillRectangle(rect, upsBrush);
							}
							else
							{
								dnsBrush.Opacity = (rd.Key == Profiles[i].poc) ? maxOpacity + 0.3f : (float)Math.Round((maxOpacity / pocVol) * rd.Value, 5);
								RenderTarget.FillRectangle(rect, dnsBrush);
							}
							
							if(drawText)
							{
								rect.Width = 100f;
							
								tfDynNorm.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
								tfDynBold.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
								
								tl = (rd.Key == Profiles[i].poc) ? new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, rd.Value.ToString(), tfDynBold, rect.Width, rect.Height) : new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, rd.Value.ToString(), tfDynNorm, rect.Width, rect.Height);
								
								tl.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
								
								vect.X = rect.X + 2f;
								vect.Y = rect.Y;
								
								if(Profiles[i].dir > 0)
								{
									RenderTarget.DrawTextLayout(vect, tl, txtBrush);
								}
								else
								{
									RenderTarget.DrawTextLayout(vect, tl, txtBrush);
								}
								
								tl.Dispose();
							}
						}
						
						if(drawBorder)
						{
							rect.X      = (float)x1;
							rect.Y      = (float)((chartScale.GetYByValue(Profiles[i].max) + chartScale.GetYByValue(Profiles[i].max + TickSize)) / 2);
							rect.Width  = (float)wd;
							rect.Height = (float)mh;
							
							if(Profiles[i].dir > 0)
							{
								upsBrush.Opacity = maxOpacity;
								RenderTarget.DrawRectangle(rect, upsBrush);
							}
							else
							{
								dnsBrush.Opacity = maxOpacity;
								RenderTarget.DrawRectangle(rect, dnsBrush);
							}
						}
					}
				}
				
				#endregion
				
				// ---
				
				chartProps = null;
				
				sfDynNorm = null;
				sfDynBold = null;
				tfDynNorm.Dispose();
				tfDynBold.Dispose();
				
				bckBrush.Dispose();
				upsBrush.Dispose();
				dnsBrush.Dispose();
				txtBrush.Dispose();
				
				RenderTarget.AntialiasMode = oldAntialiasMode;
				
			}
			catch(Exception exception)
			{
				if(logErrors)
				{
					NinjaTrader.Code.Output.Process(exception.ToString(), PrintTo.OutputTab1);
				}
			}
		}
		
		#endregion
		
		#region Text Utilities
		
		/// getTextSze
		///
		private double getTextSize(ChartScale chartScale)
		{
			float  y1 = 0f;
			float  y2 = 0f;
			float  ls = (float)ChartControl.Properties.LabelFont.Size * 2;
			float  ts = float.MaxValue;
			double tp = Instrument.MasterInstrument.RoundToTickSize(chartScale.GetValueByY(ChartPanel.Y));
			double bt = Instrument.MasterInstrument.RoundToTickSize(chartScale.GetValueByY(ChartPanel.H));
			
			for(double i=bt-TickSize;i<=tp;i+=TickSize)
			{
				y1 = ((chartScale.GetYByValue(i) + chartScale.GetYByValue(i + TickSize)) / 2);
				y2 = ((chartScale.GetYByValue(i) + chartScale.GetYByValue(i - TickSize)) / 2);
				ts = (Math.Abs(y1 - y2) < ts) ? Math.Abs(y1 - y2) : ts;
			}
			
			return (double)Math.Min(Math.Round(ts*0.6), ls);
		}
		
		#endregion
		
		#region Color Utilities
		
		/// invertColor
		///
		private SolidColorBrush invertColor(SolidColorBrush brush)
		{
			try
			{
				byte r = (byte)(255 - ((SolidColorBrush)brush).Color.R);
				byte g = (byte)(255 - ((SolidColorBrush)brush).Color.G);
				byte b = (byte)(255 - ((SolidColorBrush)brush).Color.B);
				
				return new SolidColorBrush(Color.FromRgb(r, g, b));
			}
			catch(Exception e)
			{
				if(logErrors)
				{
					Print(e.ToString());
				}
			}
			
			return Brushes.Yellow;
		}
		
		/// blendColor
		///
		private SolidColorBrush blendColor(SolidColorBrush foreBrush, SolidColorBrush backBrush, double amount)
		{
			try
			{
			    byte r = (byte) ((((SolidColorBrush)foreBrush).Color.R * amount) + ((SolidColorBrush)backBrush).Color.R * (1.0 - amount));
			    byte g = (byte) ((((SolidColorBrush)foreBrush).Color.G * amount) + ((SolidColorBrush)backBrush).Color.G * (1.0 - amount));
			    byte b = (byte) ((((SolidColorBrush)foreBrush).Color.B * amount) + ((SolidColorBrush)backBrush).Color.B * (1.0 - amount));
			    
				return new SolidColorBrush(Color.FromRgb(r, g, b));
			}
			catch(Exception e)
			{
				if(logErrors)
				{
					Print(e.ToString());
				}
			}
			
			return Brushes.Yellow;
		}
		
		#endregion
		
		#region Properties
		
		[Browsable(false)]
        [XmlIgnore]
        public Series<double> ZigZagDots
        {
            get { return Values[0]; }
        }
		
		// ---
		
		[NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Zig Zag Span", GroupName = "Parameters", Order = 0)]
        public int zzSpan
        { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Display Type", GroupName = "Parameters", Order = 1)]
		public SwingProfileDisplayType displayType
		{ get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Extend", GroupName = "Parameters", Order = 2)]
        public bool extend
        { get; set; }
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Up Swing Color", GroupName = "Parameters", Order = 3)]
		public Brush upSwingColor
		{ get; set; }
		
		[Browsable(false)]
		public string upSwingColorSerializable
		{
			get { return Serialize.BrushToString(upSwingColor); }
			set { upSwingColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Down Swing Color", GroupName = "Parameters", Order = 4)]
		public Brush dnSwingColor
		{ get; set; }
		
		[Browsable(false)]
		public string dnSwingColorSerializable
		{
			get { return Serialize.BrushToString(dnSwingColor); }
			set { dnSwingColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Text Color", GroupName = "Parameters", Order = 5)]
		public Brush textColor
		{ get; set; }
		
		[Browsable(false)]
		public string textColorSerializable
		{
			get { return Serialize.BrushToString(textColor); }
			set { textColor = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
        [Range(0.1f, 0.7f)]
        [Display(Name = "Max Opacity", GroupName = "Parameters", Order = 6)]
        public float maxOpacity
        { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Draw Border", GroupName = "Parameters", Order = 7)]
        public bool drawBorder
        { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Draw Volume", GroupName = "Parameters", Order = 8)]
        public bool drawVolume
        { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Draw Text", GroupName = "Parameters", Order = 9)]
        public bool drawText
        { get; set; }
		
		#endregion
	}
}

public enum SwingProfileDisplayType
{
	Profile,
	Map
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Infinity.SwingProfile[] cacheSwingProfile;
		public Infinity.SwingProfile SwingProfile(int zzSpan, SwingProfileDisplayType displayType, bool extend, Brush upSwingColor, Brush dnSwingColor, Brush textColor, float maxOpacity, bool drawBorder, bool drawVolume, bool drawText)
		{
			return SwingProfile(Input, zzSpan, displayType, extend, upSwingColor, dnSwingColor, textColor, maxOpacity, drawBorder, drawVolume, drawText);
		}

		public Infinity.SwingProfile SwingProfile(ISeries<double> input, int zzSpan, SwingProfileDisplayType displayType, bool extend, Brush upSwingColor, Brush dnSwingColor, Brush textColor, float maxOpacity, bool drawBorder, bool drawVolume, bool drawText)
		{
			if (cacheSwingProfile != null)
				for (int idx = 0; idx < cacheSwingProfile.Length; idx++)
					if (cacheSwingProfile[idx] != null && cacheSwingProfile[idx].zzSpan == zzSpan && cacheSwingProfile[idx].displayType == displayType && cacheSwingProfile[idx].extend == extend && cacheSwingProfile[idx].upSwingColor == upSwingColor && cacheSwingProfile[idx].dnSwingColor == dnSwingColor && cacheSwingProfile[idx].textColor == textColor && cacheSwingProfile[idx].maxOpacity == maxOpacity && cacheSwingProfile[idx].drawBorder == drawBorder && cacheSwingProfile[idx].drawVolume == drawVolume && cacheSwingProfile[idx].drawText == drawText && cacheSwingProfile[idx].EqualsInput(input))
						return cacheSwingProfile[idx];
			return CacheIndicator<Infinity.SwingProfile>(new Infinity.SwingProfile(){ zzSpan = zzSpan, displayType = displayType, extend = extend, upSwingColor = upSwingColor, dnSwingColor = dnSwingColor, textColor = textColor, maxOpacity = maxOpacity, drawBorder = drawBorder, drawVolume = drawVolume, drawText = drawText }, input, ref cacheSwingProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Infinity.SwingProfile SwingProfile(int zzSpan, SwingProfileDisplayType displayType, bool extend, Brush upSwingColor, Brush dnSwingColor, Brush textColor, float maxOpacity, bool drawBorder, bool drawVolume, bool drawText)
		{
			return indicator.SwingProfile(Input, zzSpan, displayType, extend, upSwingColor, dnSwingColor, textColor, maxOpacity, drawBorder, drawVolume, drawText);
		}

		public Indicators.Infinity.SwingProfile SwingProfile(ISeries<double> input , int zzSpan, SwingProfileDisplayType displayType, bool extend, Brush upSwingColor, Brush dnSwingColor, Brush textColor, float maxOpacity, bool drawBorder, bool drawVolume, bool drawText)
		{
			return indicator.SwingProfile(input, zzSpan, displayType, extend, upSwingColor, dnSwingColor, textColor, maxOpacity, drawBorder, drawVolume, drawText);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Infinity.SwingProfile SwingProfile(int zzSpan, SwingProfileDisplayType displayType, bool extend, Brush upSwingColor, Brush dnSwingColor, Brush textColor, float maxOpacity, bool drawBorder, bool drawVolume, bool drawText)
		{
			return indicator.SwingProfile(Input, zzSpan, displayType, extend, upSwingColor, dnSwingColor, textColor, maxOpacity, drawBorder, drawVolume, drawText);
		}

		public Indicators.Infinity.SwingProfile SwingProfile(ISeries<double> input , int zzSpan, SwingProfileDisplayType displayType, bool extend, Brush upSwingColor, Brush dnSwingColor, Brush textColor, float maxOpacity, bool drawBorder, bool drawVolume, bool drawText)
		{
			return indicator.SwingProfile(input, zzSpan, displayType, extend, upSwingColor, dnSwingColor, textColor, maxOpacity, drawBorder, drawVolume, drawText);
		}
	}
}

#endregion
