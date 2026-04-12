import { Component, inject, OnDestroy, OnInit, signal, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { NgFor, NgIf } from '@angular/common';
import { toObservable } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { skip } from 'rxjs/operators';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData, ChartOptions } from 'chart.js';
import { MatCardModule } from '@angular/material/card';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { AnalysisService, BucketCompany, DistributionBin } from '../../core/services/analysis.service';
import { LocationFilterService } from '../../core/services/location-filter.service';

interface MetricDef {
  value: string;
  label: string;
}

const STATE_KEY = 'analysisState';

@Component({
  selector: 'app-analysis',
  standalone: true,
  imports: [
    FormsModule, NgFor, NgIf,
    BaseChartDirective,
    MatCardModule, MatSelectModule, MatFormFieldModule, MatCheckboxModule,
    MatProgressSpinnerModule, MatPaginatorModule
  ],
  templateUrl: './analysis.component.html',
  styleUrl: './analysis.component.scss'
})
export class AnalysisComponent implements OnInit, OnDestroy {
  @ViewChild(BaseChartDirective) chartRef!: BaseChartDirective;

  private analysisService = inject(AnalysisService);
  private router = inject(Router);
  readonly locationFilter = inject(LocationFilterService);

  readonly metrics: MetricDef[] = [
    { value: 'activeJobs',           label: 'Active Job Postings' },
    { value: 'remoteJobs',           label: 'Remote Positions' },
    { value: 'salaryDisclosureRate', label: 'Salary Disclosure Rate' },
    { value: 'avgJobLifetimeDays',   label: 'Avg Job Lifetime (Days)' },
    { value: 'duplicateJobs',        label: 'Duplicate Postings' },
  ];

  selectedMetric = 'activeJobs';
  availableSizes = signal<string[]>([]);
  selectedSizes = new Set<string>();
  loading = signal(true);

  chartData = signal<ChartData<'bar'>>({ labels: [], datasets: [] });
  private bins: DistributionBin[] = [];

  selectedBucket = signal<string | null>(null);
  bucketCompanies = signal<BucketCompany[]>([]);
  bucketLoading = signal(false);
  bucketPage = 0;
  bucketPageSize = 10;

  pagedCompanies = () => {
    const start = this.bucketPage * this.bucketPageSize;
    return this.bucketCompanies().slice(start, start + this.bucketPageSize);
  };

  chartOptions: ChartOptions<'bar'> = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
      tooltip: {
        callbacks: {
          label: ctx => ` ${(ctx.parsed.y ?? 0).toLocaleString()} companies`
        }
      }
    },
    scales: {
      x: { ticks: { font: { size: 12 }, maxRotation: 45, minRotation: 45 } },
      y: { beginAtZero: true, ticks: { precision: 0 }, title: { display: true, text: 'Companies' } }
    }
  };

  private locationSub!: Subscription;

  constructor() {
    this.locationSub = toObservable(this.locationFilter.usOnly).pipe(skip(1)).subscribe(() => {
      this.selectedBucket.set(null);
      this.bucketCompanies.set([]);
      this.load();
    });
  }

  ngOnInit(): void {
    const raw = localStorage.getItem(STATE_KEY);
    if (raw) {
      localStorage.removeItem(STATE_KEY);
      const state = JSON.parse(raw);
      this.selectedMetric = state.selectedMetric ?? this.selectedMetric;
      this.selectedSizes  = new Set<string>(state.selectedSizes ?? []);
      this.bucketPage     = state.bucketPage ?? 0;
      this.bucketPageSize = state.bucketPageSize ?? 10;
      this.bins           = state.bins ?? [];
      this.chartData.set(this.buildChartData(this.bins));
      this.loading.set(false);
      this.analysisService.getSizes().subscribe(sizes => this.availableSizes.set(sizes));
      if (state.selectedBucket) {
        this.selectedBucket.set(state.selectedBucket);
        this.fetchBucketCompanies(state.selectedBucket);
      }
      return;
    }

    this.analysisService.getSizes().subscribe(sizes => {
      this.availableSizes.set(sizes);
      this.load();
    });
  }

  ngOnDestroy(): void {
    this.locationSub.unsubscribe();
    localStorage.setItem(STATE_KEY, JSON.stringify({
      selectedMetric: this.selectedMetric,
      selectedSizes: [...this.selectedSizes],
      selectedBucket: this.selectedBucket(),
      bucketPage: this.bucketPage,
      bucketPageSize: this.bucketPageSize,
      bins: this.bins,
    }));
  }

  onChartClick(event: MouseEvent): void {
    const chart = this.chartRef?.chart;
    if (!chart) return;
    const elements = chart.getElementsAtEventForMode(event, 'nearest', { intersect: true }, false);
    if (elements.length > 0) {
      const bucket = this.bins[elements[0].index]?.bucket;
      if (bucket) this.loadBucketCompanies(bucket);
    }
  }

  toggleSize(size: string): void {
    if (this.selectedSizes.has(size)) this.selectedSizes.delete(size);
    else this.selectedSizes.add(size);
    this.selectedBucket.set(null);
    this.bucketCompanies.set([]);
    this.load();
  }

  isSizeSelected(size: string): boolean {
    return this.selectedSizes.has(size);
  }

  get currentMetricLabel(): string {
    return this.metrics.find(m => m.value === this.selectedMetric)?.label ?? '';
  }

  onMetricChange(): void {
    this.selectedBucket.set(null);
    this.bucketCompanies.set([]);
    this.load();
  }

  goToCompany(id: number): void {
    this.router.navigate(['/companies', id], { state: { fromAnalysis: true } });
  }

  onBucketPage(e: PageEvent): void {
    this.bucketPage = e.pageIndex;
    this.bucketPageSize = e.pageSize;
  }

  private loadBucketCompanies(bucket: string): void {
    if (this.selectedBucket() === bucket) {
      this.selectedBucket.set(null);
      this.bucketCompanies.set([]);
      return;
    }
    this.selectedBucket.set(bucket);
    this.bucketPage = 0;
    this.fetchBucketCompanies(bucket);
  }

  private fetchBucketCompanies(bucket: string): void {
    this.bucketLoading.set(true);
    const isUs = this.locationFilter.usOnly() ? true : undefined;
    this.analysisService.getBucketCompanies(this.selectedMetric, bucket, [...this.selectedSizes], isUs).subscribe({
      next: result => { this.bucketCompanies.set(result.companies); this.bucketLoading.set(false); },
      error: () => this.bucketLoading.set(false)
    });
  }

  private load(): void {
    this.loading.set(true);
    const isUs = this.locationFilter.usOnly() ? true : undefined;
    this.analysisService.getDistribution(this.selectedMetric, [...this.selectedSizes], isUs).subscribe({
      next: bins => {
        this.bins = bins;
        this.chartData.set(this.buildChartData(bins));
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  private buildChartData(bins: DistributionBin[]): ChartData<'bar'> {
    return {
      labels: bins.map(b => b.bucket),
      datasets: [{
        data: bins.map(b => b.count),
        backgroundColor: 'rgba(63, 81, 181, 0.75)',
        borderColor: 'rgba(63, 81, 181, 1)',
        borderWidth: 1,
        borderRadius: 4,
        hoverBackgroundColor: 'rgba(63, 81, 181, 1)'
      }]
    };
  }
}
