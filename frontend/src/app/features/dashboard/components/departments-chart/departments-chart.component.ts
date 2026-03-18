import { Component, input, computed } from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData, ChartOptions } from 'chart.js';
import { DepartmentBucket } from '../../../../core/models/stats.model';

@Component({
  selector: 'app-departments-chart',
  standalone: true,
  imports: [BaseChartDirective],
  template: `
    <canvas baseChart
      [data]="chartData()"
      [options]="options"
      type="bar">
    </canvas>
  `
})
export class DepartmentsChartComponent {
  data = input<DepartmentBucket[]>([]);

  options: ChartOptions<'bar'> = {
    responsive: true,
    plugins: { legend: { display: false } },
    scales: { y: { beginAtZero: true } }
  };

  chartData = computed<ChartData<'bar'>>(() => ({
    labels: this.data().map(d => d.department),
    datasets: [{
      data: this.data().map(d => d.count),
      backgroundColor: 'rgba(0, 188, 212, 0.7)',
      borderColor: 'rgba(0, 188, 212, 1)',
      borderWidth: 1
    }]
  }));
}
