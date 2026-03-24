import { Component, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe, NgIf } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { JobDetail } from '../../../core/models/job.model';
import { JobsService } from '../../../core/services/jobs.service';

@Component({
  selector: 'app-job-detail-dialog',
  standalone: true,
  imports: [NgIf, DecimalPipe, MatDialogModule, MatButtonModule, MatProgressSpinnerModule, MatCardModule],
  templateUrl: './job-detail-dialog.component.html'
})
export class JobDetailDialogComponent implements OnInit {
  private jobsService = inject(JobsService);
  private data: { jobId: number } = inject(MAT_DIALOG_DATA);

  job = signal<JobDetail | null>(null);
  loading = signal(true);

  ngOnInit(): void {
    this.jobsService.getJob(this.data.jobId).subscribe({
      next: j => { this.job.set(j); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  applyNow(url: string): void {
    window.open(url, '_blank', 'noopener,noreferrer');
  }
}
