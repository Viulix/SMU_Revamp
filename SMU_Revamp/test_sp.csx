using System;
using System.Linq;
using System.Reflection;
using ScottPlot;
using ScottPlot.Plottables;

var plot = new Plot();
var sp = plot.Add.Scatter(new double[] { 1, 2 }, new double[] { 1, 2 });
foreach (var prop in sp.GetType().GetProperties()) {
    Console.WriteLine(prop.Name + " - " + prop.PropertyType.Name);
}
