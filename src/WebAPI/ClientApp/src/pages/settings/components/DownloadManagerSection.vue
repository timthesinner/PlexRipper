<template>
	<p-section>
		<template #header> {{ $t('pages.settings.advanced.download-manager.header') }} </template>
		<!--	Max segmented downloads	-->
		<v-row>
			<v-col cols="4" align-self="center">
				<help-icon help-id="help.settings.advanced.download-manager-section.download-segments" />
			</v-col>
			<v-col cols="8" align-self="center">
				<v-slider v-model="downloadSegments" min="1" max="8" dense style="height: 36px">
					<template #append>
						<p>{{ downloadSegments }}</p>
					</template>
				</v-slider>
			</v-col>
		</v-row>
	</p-section>
</template>

<script lang="ts">
import { Component, Vue } from 'vue-property-decorator';
import { timer } from 'rxjs';
import { debounce, distinctUntilChanged, map } from 'rxjs/operators';
import { SettingsService } from '@service';

@Component
export default class DownloadManagerSection extends Vue {
	downloadSegments: number = 0;

	mounted(): void {
		this.$subscribeTo(
			this.$watchAsObservable('downloadSegments').pipe(
				map((x: { oldValue: number; newValue: number }) => x.newValue),
				debounce(() => timer(1000)),
				distinctUntilChanged(),
			),
			(value) => SettingsService.updateSetting('downloadSegments', value),
		);

		this.$subscribeTo(SettingsService.getDownloadSegments(), (downloadSegments) => {
			this.downloadSegments = downloadSegments;
		});
	}
}
</script>
