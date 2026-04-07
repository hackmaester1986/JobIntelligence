import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgClass } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { AiChatComponent } from './features/dashboard/components/ai-chat/ai-chat.component';
import { ChatDialogComponent } from './features/dashboard/components/ai-chat/chat-dialog.component';
import { LocationFilterService } from './core/services/location-filter.service';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, NgClass,
    MatToolbarModule, MatButtonModule, MatIconModule, MatButtonToggleModule, MatDialogModule,
    MatMenuModule, MatDividerModule,
    AiChatComponent
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  private dialog = inject(MatDialog);
  readonly locationFilter = inject(LocationFilterService);

  openChatDialog(): void {
    this.dialog.open(ChatDialogComponent, {
      width: '92vw',
      maxWidth: '500px',
      height: 'auto',
      panelClass: 'chat-dialog-panel'
    });
  }
}
