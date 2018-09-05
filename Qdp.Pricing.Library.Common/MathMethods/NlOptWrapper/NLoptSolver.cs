﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Qdp.Pricing.Library.Common.MathMethods.NlOptWrapper
{
	/// <summary>
	/// This class wraps the NLopt C library. Be sure to dispose it when done.
	/// </summary>
	public class NLoptSolver : IDisposable
	{
		[DllImport("kernel32", SetLastError = true)]
		private static extern IntPtr LoadLibrary(string lpFileName);
        private const string NLOPT_DLL_NAME = "libnlopt-0.dll";

        static NLoptSolver()
		{
            var path = Path.Combine(IntPtr.Size > 4 ? "x64" : "x86", NLOPT_DLL_NAME);
            LoadLibrary(path);
        }

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate double nlopt_func(uint n, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0), In] double[] x, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0), In, Out] double[] gradient, IntPtr data);
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void nlopt_mfunc(uint m, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0), In, Out] double[] result, uint n, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2), In] double[] x, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2), In, Out] double[] gradient, IntPtr data);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern void nlopt_version(out int major, out int minor, out int bugfix);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr nlopt_create(NLoptAlgorithm algorithm, uint n);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern void nlopt_destroy(IntPtr opt);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
		private static extern NloptResult nlopt_optimize(IntPtr opt, double[] x, ref double result);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NloptResult nlopt_set_min_objective(IntPtr opt, nlopt_func f, IntPtr data);
		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NloptResult nlopt_set_max_objective(IntPtr opt, nlopt_func f, IntPtr data);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NLoptAlgorithm nlopt_get_algorithm(IntPtr opt);
		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern uint nlopt_get_dimension(IntPtr opt);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NloptResult nlopt_set_lower_bounds(IntPtr opt, double[] lowerBounds);
		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NloptResult nlopt_set_upper_bounds(IntPtr opt, double[] upperBounds);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NloptResult nlopt_add_inequality_constraint(IntPtr opt, nlopt_func fc, IntPtr data, double tolerance);
		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NloptResult nlopt_add_equality_constraint(IntPtr opt, nlopt_func fc, IntPtr data, double tolerance);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NloptResult nlopt_set_xtol_rel(IntPtr opt, double tolerance);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern NloptResult nlopt_set_local_optimizer(IntPtr opt, IntPtr local);

		[DllImport(NLOPT_DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
		private static extern void nlopt_set_maxeval(IntPtr opt, int maxeval);

		private IntPtr _opt;
		private readonly Dictionary<Delegate, nlopt_func> _funcCache = new Dictionary<Delegate, nlopt_func>();


		public NLoptSolver(NLoptAlgorithm algorithm, uint numVariables, double relativeStoppingTolerance = 0.0001, int maximumIterations = 0, NLoptAlgorithm? childAlgorithm = null)
		{
			if (numVariables < 1)
				throw new ArgumentOutOfRangeException("numVariables");

			_opt = nlopt_create(algorithm, numVariables);
			if (_opt == IntPtr.Zero)
				throw new ArgumentException("Unable to initialize the algorithm.", "algorithm");

			if (relativeStoppingTolerance > 0.0)
			{
				var res = nlopt_set_xtol_rel(_opt, relativeStoppingTolerance);
				if (res != NloptResult.SUCCESS)
					throw new ArgumentException("Unable to set primary tolerance. Result: " + res, "relativeStoppingTolerance");
			}

			if (maximumIterations > 0)
				nlopt_set_maxeval(_opt, maximumIterations);

			if (childAlgorithm != null)
			{
				var inner = nlopt_create(childAlgorithm.Value, numVariables);
				if (inner == IntPtr.Zero)
					throw new ArgumentException("Unable to initialize the secondary algorithm.", "childAlgorithm");

				if (relativeStoppingTolerance > 0.0)
				{
					var res = nlopt_set_xtol_rel(inner, relativeStoppingTolerance);
					if (res != NloptResult.SUCCESS)
						throw new ArgumentException("Unable to set secondary tolerance. Result: " + res, "relativeStoppingTolerance");
				}

				if (maximumIterations > 0)
					nlopt_set_maxeval(inner, maximumIterations);

				var ret = nlopt_set_local_optimizer(_opt, inner);
				nlopt_destroy(inner);

				if (ret != NloptResult.SUCCESS)
					throw new ArgumentException("Unable to associate the child optimizer. Result: " + ret, "childAlgorithm");
			}
		}

		/// <summary>
		/// Primary/outer algorithm.
		/// </summary>
		public NLoptAlgorithm Algorithm { get { return nlopt_get_algorithm(_opt); } }

		/// <summary>
		/// Number of variables.
		/// </summary>
		public uint Dimension { get { return nlopt_get_dimension(_opt); } }

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~NLoptSolver()
		{
			Dispose(false);
		}

		protected virtual void Dispose(bool isManaged)
		{
			if (isManaged)
				_funcCache.Clear();

			if (_opt != IntPtr.Zero)
			{
				nlopt_destroy(_opt);
				_opt = IntPtr.Zero;
			}
		}

		/// <summary>
		/// Check if an inequality constaint can be applied on current algorithm.
		/// In NLopt, api/options.c has a function inequality_ok() which do the same verification.
		/// </summary>
		protected void CheckInequalityConstraintAvailability()
		{
			NLoptAlgorithm algorithm = Algorithm;
			switch (algorithm)
			{
				case NLoptAlgorithm.LD_MMA:
				case NLoptAlgorithm.LD_CCSAQ:
				case NLoptAlgorithm.LD_SLSQP:
				case NLoptAlgorithm.LN_COBYLA:
				case NLoptAlgorithm.GN_ISRES:
				case NLoptAlgorithm.GN_ORIG_DIRECT:
				case NLoptAlgorithm.GN_ORIG_DIRECT_L:
				case NLoptAlgorithm.AUGLAG:
				case NLoptAlgorithm.AUGLAG_EQ:
				case NLoptAlgorithm.LN_AUGLAG:
				case NLoptAlgorithm.LN_AUGLAG_EQ:
				case NLoptAlgorithm.LD_AUGLAG:
				case NLoptAlgorithm.LD_AUGLAG_EQ:
					break;

				default:
					throw new ArgumentException("Algorithm " + algorithm.ToString() + " does not support inequality constraint.");
			}
		}

		/// <summary>
		/// Check if an equality constaint can be applied on current algorithm.
		/// In NLopt, api/options.c has a function equality_ok() which do the same verification.
		/// </summary>
		protected void CheckEqualityConstraintAvailability()
		{
			NLoptAlgorithm algorithm = Algorithm;
			switch (algorithm)
			{
				case NLoptAlgorithm.LD_SLSQP:
				case NLoptAlgorithm.GN_ISRES:
				case NLoptAlgorithm.LN_COBYLA:
					break;

				default:
					throw new ArgumentException("Algorithm " + algorithm.ToString() + " does not support equality constraint.");
			}
		}

		public void AddLessOrEqualZeroConstraint(Func<double[], double> constraint, double tolerance = 0.001)
		{
			CheckInequalityConstraintAvailability();
			nlopt_func func = (u, values, gradient, data) =>
			{
				if (gradient != null)
					throw new InvalidOperationException("Expected the constraint to handle the gradient.");
				return constraint.Invoke(values);
			};
			_funcCache.Add(constraint, func);

			var res = nlopt_add_inequality_constraint(_opt, func, IntPtr.Zero, tolerance);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to add the constraint. Result: " + res, "constraint");
		}

		/// <summary>
		/// The gradient and current variables are passed to the constraint.
		/// </summary>
		public void AddLessOrEqualZeroConstraint(Func<double[], double[], double> constraint, double tolerance = 0.001)
		{
			CheckInequalityConstraintAvailability();
			nlopt_func func = (u, values, gradient, data) => constraint.Invoke(values, gradient);
			_funcCache.Add(constraint, func);
			var res = nlopt_add_inequality_constraint(_opt, func, IntPtr.Zero, tolerance);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to add the constraint. Result: " + res, "constraint");
		}

		public void AddEqualZeroConstraint(Func<double[], double> constraint, double tolerance = 0.001)
		{
			CheckEqualityConstraintAvailability();
			nlopt_func func = (u, values, gradient, data) =>
			{
				if (gradient != null)
					throw new InvalidOperationException("Expected the constraint to handle the gradient.");
				return constraint.Invoke(values);
			};
			_funcCache.Add(constraint, func);

			var res = nlopt_add_equality_constraint(_opt, func, IntPtr.Zero, tolerance);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to add the constraint. Result: " + res, "constraint");
		}

		/// <summary>
		/// The gradient and current variables are passed to the constraint.
		/// </summary>
		public void AddEqualZeroConstraint(Func<double[], double[], double> constraint, double tolerance = 0.001)
		{
			CheckEqualityConstraintAvailability();
			nlopt_func func = (u, values, gradient, data) => constraint.Invoke(values, gradient);
			_funcCache.Add(constraint, func);
			var res = nlopt_add_equality_constraint(_opt, func, IntPtr.Zero, tolerance);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to add the constraint. Result: " + res, "constraint");
		}

		public void SetMinObjective(Func<double[], double> objective)
		{
			nlopt_func func = (u, values, gradient, data) => objective.Invoke(values);
			_funcCache.Add(objective, func);
			var res = nlopt_set_min_objective(_opt, func, IntPtr.Zero);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to set the objective function. Result: " + res, "objective");
		}

		public void SetMinObjective(Func<double[], double[], double> objective)
		{
			nlopt_func func = (u, values, gradient, data) => objective.Invoke(values, gradient);
			_funcCache.Add(objective, func);
			var res = nlopt_set_min_objective(_opt, func, IntPtr.Zero);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to set the objective function. Result: " + res, "objective");
		}

		public void SetMaxObjective(Func<double[], double> objective)
		{
			nlopt_func func = (n, values, gradient, data) => objective.Invoke(values);
			_funcCache.Add(objective, func);
			var res = nlopt_set_max_objective(_opt, func, IntPtr.Zero);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to set the objective function. Result: " + res, "objective");
		}

		public void SetMaxObjective(Func<double[], double[], double> objective)
		{
			nlopt_func func = (n, values, gradient, data) => objective.Invoke(values, gradient);
			_funcCache.Add(objective, func);
			var res = nlopt_set_max_objective(_opt, func, IntPtr.Zero);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to set the objective function. Result: " + res, "objective");
		}

		public void SetLowerBounds(params double[] minimums)
		{
			var res = nlopt_set_lower_bounds(_opt, minimums);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to set the lower bounds. Result: " + res, "minimums");
		}

		public void SetUpperBounds(params double[] maximums)
		{
			var res = nlopt_set_upper_bounds(_opt, maximums);
			if (res != NloptResult.SUCCESS)
				throw new ArgumentException("Unable to set the upper bounds. Result: " + res, "maximums");
		}

		public NloptResult Optimize(double[] initialValues, out double? finishingObjectiveScore)
		{
			double temp = 0;
			var result = nlopt_optimize(_opt, initialValues, ref temp);
			finishingObjectiveScore = temp;
			return result;
		}
	}
}
